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
var knownReports = new[]
{
    new ReportSelectors("P/E, P/B & Div.Yield", "#ddlHistoricaldivtypesubindex",  "#datepickerFromDivYield",   "#datepickerToDivYield",   "#submit_buttonDivdata",        "#exporthistoricaldiv"),
    new ReportSelectors("Historical Data",      "#ddlHistoricalsubindex",         "#datepickerFrom",           "#datepickerTo",           "#submit_button",               "#exporthistorical"),
    new ReportSelectors("VIX Data",             "#ddlHistoricalvixsubindex",      "#datepickerFromvixdata",    "#datepickerTovixdata",    "#submit_buttonvixdata",        "#exporthistoricalvix"),
    new ReportSelectors("Total Index",          "#ddlHistoricaltotalsubindex",    "#datepickerFromtotalindex", "#datepickerTototalindex", "#submit_totalindexhistorical", "#exportTotalindex")
};

// ── Start logger ──────────────────────────────────────────────────────────────
using var logger = new AppLogger();

Console.WriteLine($"Range   : {startDate:dd-MM-yyyy} to {endDate:dd-MM-yyyy}");
Console.WriteLine($"Chunks  : {chunks.Count} x {settings.ChunkMonths}-month slices");
Console.WriteLine($"Indices : {indexNames.Count}");
Console.WriteLine($"Output  : {settings.DownloadsPath}");
Console.WriteLine($"Log     : {logger.LogFilePath}");
Console.WriteLine();

logger.Info($"Range    : {startDate:dd-MM-yyyy} to {endDate:dd-MM-yyyy}");
logger.Info($"Chunks   : {chunks.Count} x {settings.ChunkMonths}-month slices");
logger.Info($"Indices  : {indexNames.Count}");

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

    // ── Step 1: manual dropdown selection ────────────────────────────────────
    Console.WriteLine();
    Console.WriteLine("╔════════════════════════════════════════════════╗");
    Console.WriteLine("║  Select the first 2 dropdowns in the browser:  ║");
    Console.WriteLine("║   1. Report Type  (e.g. Historical Data)       ║");
    Console.WriteLine("║   2. Index Type   (e.g. Equity)                ║");
    Console.WriteLine("║                                                ║");
    Console.WriteLine("║  Do NOT touch the Sub-Index or date fields.    ║");
    Console.WriteLine("║  Press ENTER when dropdowns 1–2 are set.       ║");
    Console.WriteLine("╚════════════════════════════════════════════════╝");
    Console.ReadLine();

    // ── Step 1.5: auto-detect active report type ──────────────────────────────
    Console.Write("Auto-detecting active report type... ");
    var activeSelectors = await ReportDetector.DetectActiveReportAsync(page, knownReports);

    if (activeSelectors == null)
    {
        Console.WriteLine("FAILED");
        Console.Error.WriteLine("ERROR: Could not detect the active report type. Did you select one in the browser?");
        logger.Error("Could not detect active report type — run aborted");
        return;
    }
    Console.WriteLine($"Detected '{activeSelectors.Name}'");
    logger.Info($"Report   : {activeSelectors.Name}");

    // ── Step 2: auto-detect sub-index dropdown ────────────────────────────────
    Console.Write("Locating Sub-Index dropdown... ");
    var uniqueSubIndices = indexNames
        .Select(x => x.SubIndex)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct()
        .ToList();

    var subIndexResult = await ReportDetector.DetectSubIndexDropdownAsync(
        page, activeSelectors, uniqueSubIndices);

    string       activeSubIndexSelector = subIndexResult.ActiveSelector;
    List<string> subIndexUIOptions      = subIndexResult.Options;
    bool         hasSubIndexDropdown    = subIndexResult.Found;

    if (hasSubIndexDropdown)
    {
        Console.WriteLine($"Found ({activeSubIndexSelector})");
    }
    else
    {
        Console.WriteLine("Not found. (Will process all indices flatly)");
        logger.Warn("Sub-index dropdown not found — running in flat mode");
    }
    Console.WriteLine();

    // ── Main download loop ────────────────────────────────────────────────────
    var orchestrator = new DownloadOrchestrator(
        page,
        logger,
        settings.DownloadsPath,
        settings.DelayMs,
        activeSelectors,
        activeSubIndexSelector,
        hasSubIndexDropdown,
        subIndexUIOptions,
        indexNames,
        chunks);

    await orchestrator.RunAsync();

    // ── Grand summary ─────────────────────────────────────────────────────────
    Console.WriteLine("════════════════════════════════════════════════════");
    Console.WriteLine($"  ALL DONE — {indexNames.Count} total indices processed");
    Console.WriteLine($"  Total saved   : {orchestrator.TotalSuccess}");
    Console.WriteLine($"  Total skipped : {orchestrator.TotalSkipped}  (no data for period)");
    Console.WriteLine($"  Total failed  : {orchestrator.TotalFailed}");
    Console.WriteLine($"  Folder        : {settings.DownloadsPath}");
    Console.WriteLine("════════════════════════════════════════════════════");

    logger.Info("---");
    logger.Info($"Saved   : {orchestrator.TotalSuccess}");
    logger.Info($"Skipped : {orchestrator.TotalSkipped}");
    logger.Info($"Failed  : {orchestrator.TotalFailed}");
    if (orchestrator.ServerUnresponsiveDetected)
        logger.Warn("Run stopped early — server was unresponsive");

    Console.WriteLine();
    Console.WriteLine("Press any key to close the browser...");
    Console.ReadKey(true);
}
