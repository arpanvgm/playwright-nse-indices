using Microsoft.Playwright;
using System.Text.Json;

// ═══════════════════════════════════════════════════
//  CONFIGURATION — edit before running
// ═══════════════════════════════════════════════════
const string START_DATE   = "01-05-2026";  // dd-MM-yyyy
string END_DATE           = DateTime.Now.ToString("dd-MM-yyyy"); // runtime — today's date
const int    CHUNK_MONTHS = 12;            // months per request (lower if site rejects)
const int    DELAY_MS     = 4000;          // polite pause between downloads (ms)

// ═══════════════════════════════════════════════════
//  INDEX NAMES TO DOWNLOAD  — loaded from indices.json
// ═══════════════════════════════════════════════════
var indicesFilePath = Path.Combine(AppContext.BaseDirectory, "indices.json");
if (!File.Exists(indicesFilePath))
{
    Console.Error.WriteLine($"ERROR: indices.json not found at: {indicesFilePath}");
    return;
}

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var INDEX_NAMES = JsonSerializer.Deserialize<List<IndexConfig>>(
    File.ReadAllText(indicesFilePath), jsonOptions) ?? new List<IndexConfig>();

if (INDEX_NAMES.Count == 0)
{
    Console.Error.WriteLine("ERROR: indices.json is empty or contains no index names.");
    return;
}

// ═══════════════════════════════════════════════════
//  REPORT CONFIGURATIONS
//  (SubIndexDropdown acts as a fallback if auto-discovery fails)
// ═══════════════════════════════════════════════════
var KNOWN_REPORTS = new[]
{
    new ReportSelectors("P/E, P/B & Div.Yield", "#ddlHistoricaldivtypesubindex", "#datepickerFromDivYield", "#datepickerToDivYield", "#submit_buttonDivdata", "#exporthistoricaldiv"),
    new ReportSelectors("Historical Data",      "#ddlHistoricalsubindex",        "#datepickerFrom",         "#datepickerTo",         "#submit_button",        "#exporthistorical"),
    new ReportSelectors("VIX Data",             "#ddlHistoricalvixsubindex",     "#datepickerFromvixdata",  "#datepickerTovixdata",  "#submit_buttonvixdata", "#exporthistoricalvix"),
    new ReportSelectors("Total Index",          "#ddlHistoricaltotalsubindex",   "#datepickerFromtotalindex","#datepickerTototalindex","#submit_totalindexhistorical", "#exportTotalindex")
};

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
Console.WriteLine("║  Select the first 2 dropdowns in the browser:  ║");
Console.WriteLine("║   1. Report Type  (e.g. Historical Data)       ║");
Console.WriteLine("║   2. Index Type   (e.g. Equity)                ║");
Console.WriteLine("║                                                ║");
Console.WriteLine("║  Do NOT touch the Sub-Index or date fields.    ║");
Console.WriteLine("║  Press ENTER when dropdowns 1–2 are set.       ║");
Console.WriteLine("╚════════════════════════════════════════════════╝");
Console.ReadLine();

// ── Step 1.5: Auto-detect active report type based on visible elements ────────
Console.Write("Auto-detecting active report type... ");
ReportSelectors activeSelectors = null;
foreach (var report in KNOWN_REPORTS)
{
    var isVisible = await page.EvaluateAsync<bool>($@"() => {{
        const el = document.querySelector('{report.From}');
        return el && el.offsetParent !== null;
    }}");

    if (isVisible)
    {
        activeSelectors = report;
        break;
    }
}

if (activeSelectors == null)
{
    Console.WriteLine("FAILED");
    Console.Error.WriteLine("ERROR: Could not detect the active report type. Did you select one in the browser?");
    return;
}
Console.WriteLine($"Detected '{activeSelectors.Name}'");

// ── Step 2: Smart Auto-detect the Sub-Index Dropdown ──────────────────────────
Console.Write("Locating Sub-Index dropdown... ");

// Extract unique sub-index categories from the JSON config
var uniqueSubIndices = INDEX_NAMES
    .Select(x => x.SubIndex)
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Distinct()
    .ToList();

// Let the browser scan all visible dropdowns and find the one containing our categories
var subIndexDetection = await page.EvaluateAsync<JsonElement>($@"(subs) => {{
    const normalize = v => (v || '').replace(/\s+/g, ' ').trim().toLowerCase();
    const targetSubs = subs.map(normalize);
    
    const selects = Array.from(document.querySelectorAll('select')).filter(s => s.offsetParent !== null);
    
    for (let i = 0; i < selects.length; i++) {{
        const select = selects[i];
        const optionTexts = Array.from(select.options).map(o => o.textContent.trim());
        const normalizedOptions = optionTexts.map(normalize);
        
        // Match if this dropdown contains ANY of the known sub-indices from our JSON
        if (targetSubs.some(sub => normalizedOptions.includes(sub))) {{
            return {{
                found: true,
                selector: select.id ? '#' + select.id : null,
                options: optionTexts.filter(o => o && o !== '0' && !o.toLowerCase().includes('select'))
            }};
        }}
    }}
    return {{ found: false }};
}}", uniqueSubIndices);

bool hasSubIndexDropdown = subIndexDetection.TryGetProperty("found", out var f) && f.GetBoolean();
var subIndexUIOptions = new List<string>();
string activeSubIndexSelector = activeSelectors.SubIndexDropdown; // fallback

if (hasSubIndexDropdown)
{
    var selElement = subIndexDetection.GetProperty("selector");
    if (selElement.ValueKind == JsonValueKind.String)
    {
        activeSubIndexSelector = selElement.GetString();
    }
    
    Console.WriteLine($"Found by data match ({activeSubIndexSelector})");
    foreach (var opt in subIndexDetection.GetProperty("options").EnumerateArray())
    {
        subIndexUIOptions.Add(opt.GetString());
    }
}
else
{
    Console.WriteLine($"Not found by data. Falling back to explicit ID ({activeSelectors.SubIndexDropdown})...");
    
    hasSubIndexDropdown = await page.EvaluateAsync<bool>($@"() => {{
        const el = document.querySelector('{activeSelectors.SubIndexDropdown}');
        return el && el.offsetParent !== null;
    }}");
    
    if (hasSubIndexDropdown)
    {
        subIndexUIOptions = await page.EvaluateAsync<List<string>>($@"(sel) => {{
            const select = document.querySelector(sel);
            return Array.from(select.options)
                .filter(o => o.value && o.value !== '0' && !o.text.toLowerCase().includes('select'))
                .map(o => o.textContent.trim());
        }}", activeSelectors.SubIndexDropdown);
    }
    else
    {
        Console.WriteLine("Still not found or hidden. (Will process all indices flatly)");
        subIndexUIOptions.Add("DEFAULT_FLAT_MODE");
    }
}
Console.WriteLine();

// ── Main loop: iterate through UI Categories ──────────────────────────────────
int totalSuccess = 0, totalSkipped = 0, totalFailed = 0;

foreach (var currentCategory in subIndexUIOptions)
{
    List<IndexConfig> targetIndices;

    if (hasSubIndexDropdown)
    {
        // Filter our JSON config to see if we have indices for this specific Sub-Index
        targetIndices = INDEX_NAMES
            .Where(x => string.Equals(x.SubIndex, currentCategory, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (targetIndices.Count == 0)
            continue; // Skip this category, we have nothing to download here.

        Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine($"  CATEGORY: {currentCategory}  ({targetIndices.Count} indices)");
        Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.Write($"  Changing UI to '{currentCategory}' ... ");

        // Set up the AJAX listener to wait for the Index Name dropdown to populate
        var ajaxCompleted = new TaskCompletionSource<bool>();
        EventHandler<IResponse> responseHandler = (_, response) =>
        {
            if (response.Url.Contains("Backpage.aspx") && response.Status == 200)
                ajaxCompleted.TrySetResult(true);
        };
        page.Response += responseHandler;

        var selectedCategory = await SelectDropdownByText(page, currentCategory, activeSubIndexSelector);
        if (!selectedCategory)
        {
            Console.WriteLine("FAILED — could not select category in UI. Skipping.");
            page.Response -= responseHandler;
            Console.WriteLine();
            continue;
        }

        try { await Task.WhenAny(ajaxCompleted.Task, Task.Delay(10_000)); }
        finally { page.Response -= responseHandler; }

        await page.WaitForTimeoutAsync(1000); // DOM buffer
        Console.WriteLine("done.\n");
    }
    else
    {
        targetIndices = INDEX_NAMES; // Flat mode
        Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine($"  PROCESSING ALL INDICES  ({targetIndices.Count})");
        Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    }

    // ── Download loop for the filtered indices in this category ───────────────
    for (int idx = 0; idx < targetIndices.Count; idx++)
    {
        var indexName = targetIndices[idx].Name;

        Console.Write($"  [{idx + 1}/{targetIndices.Count}]  {indexName}  -  Selecting '{indexName}' ... ");
        
        // Find the index in ANY visible dropdown (auto-discovery)
        var selected = await SelectDropdownByText(page, indexName, null);
        if (!selected)
        {
            Console.WriteLine("FAILED — option not found. Skipping.");
            continue;
        }
        await page.WaitForTimeoutAsync(600);
        Console.WriteLine("done.");

        int success = 0, skipped = 0, failed = 0;
        var safeFileName = MakeSafeFileName(indexName);

        for (int i = 0; i < chunks.Count; i++)
        {
            var (from, to) = chunks[i];
            var fromStr    = from.ToString("dd-MM-yyyy");
            var toStr      = to.ToString("dd-MM-yyyy");

            Console.Write($"    [{i + 1}/{chunks.Count}] {fromStr} to {toStr} ... ");

            try
            {
                await SetDate(page, activeSelectors.From, fromStr);
                await SetDate(page, activeSelectors.To,   toStr);
                await page.WaitForTimeoutAsync(500);

                var ajaxCompleted = new TaskCompletionSource<bool>();
                EventHandler<IResponse> responseHandler = (_, response) =>
                {
                    if (response.Url.Contains("Backpage.aspx") && response.Status == 200)
                        ajaxCompleted.TrySetResult(true);
                };
                page.Response += responseHandler;

                await JsClick(page, activeSelectors.Submit);

                try { await Task.WhenAny(ajaxCompleted.Task, Task.Delay(15_000)); }
                finally { page.Response -= responseHandler; }

                await page.WaitForTimeoutAsync(500);

                var csvVisible = await page.IsVisibleAsync(activeSelectors.Csv);

                if (!csvVisible)
                {
                    Console.WriteLine("Skipped (no data for this period)");
                    skipped++;
                    if (i < chunks.Count - 1)
                        await page.WaitForTimeoutAsync(DELAY_MS);
                    continue;
                }

                var dlTask = page.WaitForDownloadAsync(new() { Timeout = 15_000 });
                await JsClick(page, activeSelectors.Csv);
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
    }
    Console.WriteLine();
}

// ── Grand summary ─────────────────────────────────────────────────────────────
Console.WriteLine("════════════════════════════════════════════════════");
Console.WriteLine($"  ALL DONE — {INDEX_NAMES.Count} total indices processed");
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
/// Selects an option by scanning specific selector (if provided) or all visible dropdowns.
/// </summary>
static async Task<bool> SelectDropdownByText(IPage page, string optionText, string specificSelector = null)
{
    return await page.EvaluateAsync<bool>(@"(args) => {
        const { text, specificSel } = args;
        const normalize = v => (v || '').replace(/\s+/g, ' ').trim();

        const selects = specificSel 
            ? Array.from(document.querySelectorAll(specificSel))
            : Array.from(document.querySelectorAll('select'));

        for (const select of selects) {
            // Skip hidden dropdowns
            if (select.offsetParent === null) continue;

            const option = Array.from(select.options)
                .find(o => normalize(o.textContent) === normalize(text));

            if (option) {
                select.value = option.value;

                if (typeof jQuery !== 'undefined') {
                    jQuery(select).val(option.value).trigger('change');
                } else {
                    select.dispatchEvent(new Event('input',  { bubbles: true }));
                    select.dispatchEvent(new Event('change', { bubbles: true }));
                }
                return true;
            }
        }
        return false;
    }", new { text = optionText, specificSel = specificSelector });
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

// ────────────────────────────────────────────────────────────────────────────
//  RECORDS & TYPES
// ────────────────────────────────────────────────────────────────────────────
public record ReportSelectors(string Name, string SubIndexDropdown, string From, string To, string Submit, string Csv);
public record IndexConfig(string Name, string SubIndex);
