using Microsoft.Playwright;

// ═══════════════════════════════════════════════════
//  CONFIGURATION — edit before running
// ═══════════════════════════════════════════════════
//const string START_DATE   = "01-01-1996";  // dd-MM-yyyy
const string START_DATE   = "01-05-2026";  // dd-MM-yyyy
string END_DATE           =  DateTime.Now.ToString("dd-MM-yyyy");  // runtime — today's date
const int    CHUNK_MONTHS = 12;            // months per request (lower if site rejects)
const int    DELAY_MS     = 4000;          // polite pause between downloads (ms)

// ═══════════════════════════════════════════════════
//  INDEX NAMES TO DOWNLOAD  (Index Name dropdown)
//  Must match site option text exactly (ALL CAPS).
//  All names must share the same Index Type and
//  Sub-Index selected manually in the browser.
// ═══════════════════════════════════════════════════
var INDEX_NAMES = new List<string>
{
    "NIFTY 50",
    "NIFTY NEXT 50",
    "NIFTY SMALLCAP 250",
    "NIFTY SMALLCAP 50",
    "NIFTY MIDCAP 150",
    // ── add more index names here ─────────────────
};

// ═══════════════════════════════════════════════════
//  SELECTORS  (default: P/E, P/B & Div.Yield report)
//  See readme.md for selectors for other report types
// ═══════════════════════════════════════════════════
const string FROM_SEL   = "#datepickerFromDivYield";
const string TO_SEL     = "#datepickerToDivYield";
const string SUBMIT_SEL = "#submit_buttonDivdata";
const string CSV_SEL    = "#exporthistoricaldiv";

// ID of the Index Name dropdown (confirmed from live DOM)
const string INDEX_DD_SEL = "#ddlHistoricaldivtypeeindex";
// ══════════════════════════════════════════════════

var downloadsPath = @"D:\MarketData\NseDownloads";

var startDate = DateTime.ParseExact(START_DATE, "dd-MM-yyyy", null);
var endDate   = DateTime.ParseExact(END_DATE,   "dd-MM-yyyy", null);

// Build date chunks
var chunks = new List<(DateTime From, DateTime To)>();
var cursor = startDate;
while (cursor <= endDate)
{
    var chunkEnd = cursor.AddMonths(CHUNK_MONTHS).AddDays(-1);
    if (chunkEnd > endDate) chunkEnd = endDate;
    chunks.Add((cursor, chunkEnd));
    cursor = chunkEnd.AddDays(1);
}

Console.WriteLine($"Range   : {START_DATE} to {END_DATE}");
Console.WriteLine($"Chunks  : {chunks.Count} x {CHUNK_MONTHS}-month slices");
Console.WriteLine($"Indices : {INDEX_NAMES.Count}");
Console.WriteLine($"Output  : {downloadsPath}");
Console.WriteLine();

// ── Launch browser ────────────────────────────────────────────────────────────
using var playwright = await Playwright.CreateAsync();

await using var browser = await playwright.Chromium.LaunchAsync(new()
{
    Headless = false,
    Channel  = "msedge",
    Args     = new[] { "--disable-blink-features=AutomationControlled" }
});

await using var context = await browser.NewContextAsync(new() { AcceptDownloads = true });

await context.AddInitScriptAsync(
    "Object.defineProperty(navigator, 'webdriver', { get: () => undefined });");

var page = await context.NewPageAsync();

Console.WriteLine("Opening page...");
try
{
    await page.GotoAsync("https://niftyindices.com/reports/historical-data",
        new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
}
catch (TimeoutException)
{
    // Third-party scripts hang — page content is ready, continue
}
Console.WriteLine("Page loaded.");

// ── Step 1: manual selection of Report Type, Index Type, Sub-Index ────────────
Console.WriteLine();
Console.WriteLine("╔════════════════════════════════════════════════╗");
Console.WriteLine("║  Select the first 3 dropdowns in the browser: ║");
Console.WriteLine("║   1. Report Type  (e.g. P/E, P/B & Div.Yield) ║");
Console.WriteLine("║   2. Index Type   (e.g. Equity)               ║");
Console.WriteLine("║   3. Sub-Index    (e.g. Broad Market Indices)  ║");
Console.WriteLine("║                                                ║");
Console.WriteLine("║  Do NOT touch the Index Name or date fields.  ║");
Console.WriteLine("║  Press ENTER when dropdowns 1–3 are set.      ║");
Console.WriteLine("╚════════════════════════════════════════════════╝");
Console.ReadLine();

// ── Main loop: iterate through all configured index names ─────────────────────
int totalSuccess = 0, totalSkipped = 0, totalFailed = 0;

for (int idx = 0; idx < INDEX_NAMES.Count; idx++)
{
    var indexName = INDEX_NAMES[idx];

    Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    Console.WriteLine($"  [{idx + 1}/{INDEX_NAMES.Count}]  {indexName}");
    Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

    // Auto-select Index Name dropdown by its known element ID
    Console.Write($"  Selecting '{indexName}' ... ");
    var selected = await SelectDropdownById(page, INDEX_DD_SEL, indexName);
    if (!selected)
    {
        Console.WriteLine("FAILED — option not found. Skipping.");
        Console.WriteLine();
        continue;
    }
    await page.WaitForTimeoutAsync(600);
    Console.WriteLine("done.");
    Console.WriteLine();

    // ── Download loop for this index ──────────────────────────────────────────
    int success = 0, skipped = 0, failed = 0;
    var safeFileName = MakeSafeFileName(indexName);

    for (int i = 0; i < chunks.Count; i++)
    {
        var (from, to) = chunks[i];
        var fromStr    = from.ToString("dd-MM-yyyy");
        var toStr      = to.ToString("dd-MM-yyyy");

        Console.Write($"  [{i + 1}/{chunks.Count}] {fromStr} to {toStr} ... ");

        try
        {
            await SetDate(page, FROM_SEL, fromStr);
            await SetDate(page, TO_SEL,   toStr);
            await page.WaitForTimeoutAsync(500);

            // Listen for AJAX response BEFORE clicking Submit
            var ajaxCompleted = new TaskCompletionSource<bool>();
            EventHandler<IResponse> responseHandler = (_, response) =>
            {
                if (response.Url.Contains("Backpage.aspx") && response.Status == 200)
                    ajaxCompleted.TrySetResult(true);
            };
            page.Response += responseHandler;

            await JsClick(page, SUBMIT_SEL);

            try
            {
                await Task.WhenAny(ajaxCompleted.Task, Task.Delay(15_000));
            }
            finally
            {
                page.Response -= responseHandler;
            }

            await page.WaitForTimeoutAsync(500);

            var csvVisible = await page.IsVisibleAsync(CSV_SEL);
            if (!csvVisible)
            {
                Console.WriteLine("Skipped (no data for this period)");
                skipped++;
                if (i < chunks.Count - 1)
                    await page.WaitForTimeoutAsync(DELAY_MS);
                continue;
            }

            var dlTask = page.WaitForDownloadAsync(new() { Timeout = 15_000 });
            await JsClick(page, CSV_SEL);
            var dl = await dlTask;

            var filename = $"{safeFileName}_{fromStr.Replace("-", "")}_{toStr.Replace("-", "")}.csv";
            await dl.SaveAsAsync(Path.Combine(downloadsPath, filename));
            Console.WriteLine($"Saved: {filename}");
            success++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: {ex.Message}");
            failed++;
            await page.WaitForTimeoutAsync(DELAY_MS * 2);
            continue;
        }

        if (i < chunks.Count - 1)
            await page.WaitForTimeoutAsync(DELAY_MS);
    }

    totalSuccess += success;
    totalSkipped += skipped;
    totalFailed  += failed;

    Console.WriteLine();
    Console.WriteLine($"  Index   : {indexName}");
    Console.WriteLine($"  Saved   : {success}");
    Console.WriteLine($"  Skipped : {skipped}  (no data for period)");
    Console.WriteLine($"  Failed  : {failed}");
    Console.WriteLine();
}

// ── Grand summary ─────────────────────────────────────────────────────────────
Console.WriteLine("════════════════════════════════════════════════════");
Console.WriteLine($"  ALL DONE — {INDEX_NAMES.Count} indices processed");
Console.WriteLine($"  Total saved   : {totalSuccess}");
Console.WriteLine($"  Total skipped : {totalSkipped}  (no data for period)");
Console.WriteLine($"  Total failed  : {totalFailed}");
Console.WriteLine($"  Folder        : {downloadsPath}");
Console.WriteLine("════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine("Press any key to close the browser...");
Console.ReadKey(true);

// ────────────────────────────────────────────────────────────────────────────
//  HELPERS
// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Selects an option in a dropdown by its element ID, matching on option text.
/// Uses jQuery trigger if available, otherwise native DOM events.
/// Returns true if found and selected, false if option text not found.
/// </summary>
static async Task<bool> SelectDropdownById(IPage page, string selector, string optionText)
{
    return await page.EvaluateAsync<bool>(@"(args) => {
        const { sel, text } = args;
        const normalize = v => (v || '').replace(/\s+/g, ' ').trim();

        const select = document.querySelector(sel);
        if (!select) return false;

        const option = Array.from(select.options)
            .find(o => normalize(o.textContent) === normalize(text));

        if (!option) return false;

        select.value = option.value;

        if (typeof jQuery !== 'undefined') {
            jQuery(select).val(option.value).trigger('change');
        } else {
            select.dispatchEvent(new Event('input',  { bubbles: true }));
            select.dispatchEvent(new Event('change', { bubbles: true }));
        }
        return true;
    }", new { sel = selector, text = optionText });
}

static async Task SetDate(IPage page, string selector, string value)
{
    var parts = value.Split('-');
    var day   = parts[0];
    var month = parts[1];
    var year  = parts[2];

    await page.EvaluateAsync($@"() => {{
        const sel = '{selector}';
        const id  = sel.replace('#', '');

        if (typeof jQuery !== 'undefined' && jQuery('#' + id).datepicker) {{
            try {{
                jQuery('#' + id).datepicker('setDate', new Date({year}, {month} - 1, {day}));
                return;
            }} catch(e) {{}}
        }}

        const el = document.querySelector(sel);
        if (!el) return;
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
        setter.call(el, '{value}');
        el.dispatchEvent(new Event('input',  {{ bubbles: true }}));
        el.dispatchEvent(new Event('change', {{ bubbles: true }}));
        el.dispatchEvent(new Event('blur',   {{ bubbles: true }}));
    }}");
}

static async Task JsClick(IPage page, string selector)
{
    await page.EvaluateAsync($@"() => {{
        const el = document.querySelector('{selector}');
        if (el) el.click();
    }}");
}

static string MakeSafeFileName(string value)
{
    var invalid = Path.GetInvalidFileNameChars();
    return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
}
