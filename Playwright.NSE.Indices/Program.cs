using Microsoft.Playwright;
using Playwright.NSE.Indices.Browser;
using Playwright.NSE.Indices.Configuration;
using Playwright.NSE.Indices.Downloading;
using Playwright.NSE.Indices.Logging;
using Playwright.NSE.Indices.Models;
using System.Text.Json;

// ── Load configuration ────────────────────────────────────────────────────────
var settings = SettingsLoader.Load();

var startDate = new DateTime(settings.StartYear, 1, 1);
var endDate   = string.IsNullOrWhiteSpace(settings.EndDate)
    ? DateTime.Now
    : DateTime.ParseExact(settings.EndDate, "dd-MM-yyyy", null);

// ── Load indices.json ─────────────────────────────────────────────────────────
var indicesFilePath = Path.Combine(AppContext.BaseDirectory, "indices.json");
if (!File.Exists(indicesFilePath))
{
    Console.Error.WriteLine($"ERROR: indices.json not found at: {indicesFilePath}");
    return;
}

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var allIndices  = JsonSerializer.Deserialize<List<IndexConfig>>(
    File.ReadAllText(indicesFilePath), jsonOptions) ?? new List<IndexConfig>();

var indexNames = allIndices.Where(x => x.Enabled).ToList();
if (indexNames.Count == 0)
{
    Console.Error.WriteLine("ERROR: indices.json is empty or contains no enabled index names.");
    return;
}

// ── Build date chunks ─────────────────────────────────────────────────────────
var chunks = DateChunker.Build(startDate, endDate, settings.ChunkMonths);

// ── Known report configurations ───────────────────────────────────────────────
// SectionSelector  : the LI element that activates the report section in the UI.
// IndexTypeDropdown: dropdown 1 — "Select an Index Type" (value = "Equity").
// SubIndexDropdown : dropdown 2 — "Select a Sub-IndexType".
// IndexNameDropdown: dropdown 3 — "Select an Index".
// All selector IDs confirmed from live DOM inspection.
var reportsToRun = new[]
{
    new ReportSelectors(
        Name:               "Historical Data",
        SectionSelector:    "li.form1",
        IndexTypeDropdown:  "#ddlHistoricaltypee",
        SubIndexDropdown:   "#ddlHistoricaltypeeSubindex",
        IndexNameDropdown:  "#ddlHistoricaltypeeindex",
        From:               "#datepickerFrom",
        To:                 "#datepickerTo",
        Submit:             "#submit_button",
        Csv:                "#exporthistorical",
        AjaxUrlFragment:    "Backpage.aspx"),

    new ReportSelectors(
        Name:               "P/E, P/B & Div.Yield",
        SectionSelector:    "li.form4",
        IndexTypeDropdown:  "#ddlHistoricaldivtypee",
        SubIndexDropdown:   "#ddlHistoricaldivtypeeSubindex",
        IndexNameDropdown:  "#ddlHistoricaldivtypeeindex",
        From:               "#datepickerFromDivYield",
        To:                 "#datepickerToDivYield",
        Submit:             "#submit_buttonDivdata",
        Csv:                "#exporthistoricaldiv",
        AjaxUrlFragment:    "Backpage.aspx"),
};

// ── Start logger ──────────────────────────────────────────────────────────────
using var logger = new AppLogger();

Console.WriteLine($"Range   : {startDate:dd-MM-yyyy} to {endDate:dd-MM-yyyy}");
Console.WriteLine($"Chunks  : {chunks.Count} x {settings.ChunkMonths}-month slices");
Console.WriteLine($"Indices : {indexNames.Count}");
Console.WriteLine($"Reports : {reportsToRun.Length} ({string.Join(", ", reportsToRun.Select(r => r.Name))})");
Console.WriteLine($"Output  : {settings.DownloadsPath}");
Console.WriteLine($"Log     : {logger.LogFilePath}");
Console.WriteLine();

logger.Info($"Range    : {startDate:dd-MM-yyyy} to {endDate:dd-MM-yyyy}");
logger.Info($"Chunks   : {chunks.Count} x {settings.ChunkMonths}-month slices");
logger.Info($"Indices  : {indexNames.Count}");
logger.Info($"Reports  : {string.Join(", ", reportsToRun.Select(r => r.Name))}");

// ── Launch browser ────────────────────────────────────────────────────────────
using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();

var browserAndContext = await BrowserFactory.CreateAsync(playwright);
IBrowser        browser = browserAndContext.Browser;
IBrowserContext context = browserAndContext.Context;

await using (browser)
await using (context)
{
    var page = await context.NewPageAsync();
    await BrowserFactory.BlockHeavyAssetsAsync(page);

    try
    {
        await page.GotoAsync("https://niftyindices.com/reports/historical-data",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15_000 });
    }
    catch (TimeoutException)
    {
        // Third-party analytics scripts hang — page content is ready, continue
    }
    Console.WriteLine("Page loaded.");
    Console.WriteLine();

    // ── Totals across all reports ─────────────────────────────────────────────
    int grandSuccess = 0, grandSkipped = 0, grandFailed = 0;

    // ── Outer loop: one iteration per report type ─────────────────────────────
    foreach (var report in reportsToRun)
    {
        Console.WriteLine($"╔════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  REPORT: {report.Name,-42}║");
        Console.WriteLine($"╚════════════════════════════════════════════════════╝");

        logger.Info($"--- Starting report: {report.Name} ---");

        // ── Step 1 + 2: automated section click + Index Type selection ────────
        var navResult = await StartupNavigator.PrepareReportAsync(page, report);

        if (!navResult.Success)
        {
            logger.Error($"Startup navigation failed for '{report.Name}': {navResult.FailureReason}");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Aborting entire run — startup navigation failed.");
            Console.ResetColor();
            Console.WriteLine();

            // Log grand totals so far before exiting
            logger.Warn($"Run aborted during startup of '{report.Name}'");
            logger.Info($"Saved so far   : {grandSuccess}");
            logger.Info($"Skipped so far : {grandSkipped}");
            logger.Info($"Failed so far  : {grandFailed}");
            goto RunAborted;
        }

        // ── Step 3: detect sub-index dropdown ─────────────────────────────────
        Console.Write("  Locating Sub-Index dropdown... ");
        var uniqueSubIndices = indexNames
            .Select(x => x.SubIndex)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        var subIndexResult = await ReportDetector.DetectSubIndexDropdownAsync(
            page, report, uniqueSubIndices);

        string       activeSubIndexSelector = subIndexResult.ActiveSelector;
        List<string> subIndexUIOptions      = subIndexResult.Options;
        bool         hasSubIndexDropdown    = subIndexResult.Found;

        if (hasSubIndexDropdown)
            Console.WriteLine($"Found ({activeSubIndexSelector})");
        else
        {
            Console.WriteLine("Not found. (Will process all indices flatly)");
            logger.Warn($"[{report.Name}] Sub-index dropdown not found — running in flat mode");
        }
        Console.WriteLine();

        // ── Step 4: run the download loop for this report ─────────────────────
        var orchestrator = new DownloadOrchestrator(
            page,
            logger,
            settings.DownloadsPath,
            settings.DelayMs,
            report,
            activeSubIndexSelector,
            hasSubIndexDropdown,
            subIndexUIOptions,
            indexNames,
            chunks);

        await orchestrator.RunAsync();

        grandSuccess += orchestrator.TotalSuccess;
        grandSkipped += orchestrator.TotalSkipped;
        grandFailed  += orchestrator.TotalFailed;

        logger.Info($"[{report.Name}] Saved: {orchestrator.TotalSuccess}  Skipped: {orchestrator.TotalSkipped}  Failed: {orchestrator.TotalFailed}");

        // ── If the server went unresponsive, abort — no point starting next report
        if (orchestrator.ServerUnresponsiveDetected)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  Server became unresponsive during '{report.Name}'. Aborting remaining reports.");
            Console.ResetColor();
            logger.Warn($"Run aborted after server unresponsive during '{report.Name}'");
            goto RunAborted;
        }

        Console.WriteLine();
    }

    // ── Grand summary (all reports completed normally) ────────────────────────
    Console.WriteLine("════════════════════════════════════════════════════════");
    Console.WriteLine($"  ALL REPORTS DONE");
    Console.WriteLine($"  Total saved   : {grandSuccess}");
    Console.WriteLine($"  Total skipped : {grandSkipped}  (no data for period)");
    Console.WriteLine($"  Total failed  : {grandFailed}");
    Console.WriteLine($"  Folder        : {settings.DownloadsPath}");
    Console.WriteLine("════════════════════════════════════════════════════════");

    logger.Info("---");
    logger.Info($"Grand total saved   : {grandSuccess}");
    logger.Info($"Grand total skipped : {grandSkipped}");
    logger.Info($"Grand total failed  : {grandFailed}");

    goto Done;

    RunAborted:
    Console.WriteLine("════════════════════════════════════════════════════════");
    Console.WriteLine($"  RUN ABORTED");
    Console.WriteLine($"  Saved so far   : {grandSuccess}");
    Console.WriteLine($"  Skipped so far : {grandSkipped}  (no data for period)");
    Console.WriteLine($"  Failed so far  : {grandFailed}");
    Console.WriteLine($"  Folder         : {settings.DownloadsPath}");
    Console.WriteLine($"  See log for details: {logger.LogFilePath}");
    Console.WriteLine("════════════════════════════════════════════════════════");

    Done:
    Console.WriteLine();
    Console.WriteLine("Press any key to close the browser...");
    Console.ReadKey(true);
}
