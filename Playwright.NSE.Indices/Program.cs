using Microsoft.Playwright;

// ═══════════════════════════════════════════════════
//  CONFIGURATION — edit before running
// ═══════════════════════════════════════════════════
const string START_DATE   = "01-01-2000";  // dd-MM-yyyy
string END_DATE           = DateTime.Now.ToString("dd-MM-yyyy");  // runtime — today's date
const int    CHUNK_MONTHS = 12;            // months per request (lower if site rejects)
const int    DELAY_MS     = 4000;          // polite pause between downloads (ms)
// ═══════════════════════════════════════════════════

// Selectors confirmed from live DOM scan
const string FROM_SEL   = "#datepickerFromDivYield";
const string TO_SEL     = "#datepickerToDivYield";
const string SUBMIT_SEL = "#submit_buttonDivdata";
const string CSV_SEL    = "#exporthistoricaldiv";
// ══════════════════════════════════════════════════

var downloadsPath = @"D:\MarketData\Downloads";

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

Console.WriteLine($"Range  : {START_DATE} to {END_DATE}");
Console.WriteLine($"Chunks : {chunks.Count} x {CHUNK_MONTHS}-month slices");
Console.WriteLine($"Output : {downloadsPath}");
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

// ── Step 1: manual dropdown selection ────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("╔═══════════════════════════════════════════════╗");
Console.WriteLine("║  Select ALL dropdowns in the browser:         ║");
Console.WriteLine("║   1. Report Type  (P/E, P/B & Div.Yield)      ║");
Console.WriteLine("║   2. Index Type   (e.g. Equity)               ║");
Console.WriteLine("║   3. Sub-Index    (e.g. Sectoral Indices)     ║");
Console.WriteLine("║   4. Index Name   (e.g. Nifty Bank)           ║");
Console.WriteLine("║                                               ║");
Console.WriteLine("║  Do NOT touch the date fields.                ║");
Console.WriteLine("║  Press ENTER when all 4 dropdowns are set.    ║");
Console.WriteLine("╚═══════════════════════════════════════════════╝");
Console.ReadLine();
    
// ── Main loop: support multiple indices ──────────────────────────────────────
bool runAnotherCycle = true;
while (runAnotherCycle)
{
    
    var selectedIndexName = await GetSelectedDropdownText(page, 4);
    Console.WriteLine($"Selected Index Name: {selectedIndexName}");
    Console.WriteLine();

    // ── Step 2: automated download loop ──────────────────────────────────────────
    Console.WriteLine("Starting automated download loop...");
    Console.WriteLine();

    int success = 0, skipped = 0, failed = 0;
    var safeFileName = MakeSafeFileName(selectedIndexName);

    for (int i = 0; i < chunks.Count; i++)
    {
        var (from, to) = chunks[i];
        var fromStr    = from.ToString("dd-MM-yyyy");
        var toStr      = to.ToString("dd-MM-yyyy");

        Console.Write($"[{i+1}/{chunks.Count}] {fromStr} to {toStr} ... ");

        try
        {
            // Set From / To dates
            await SetDate(page, FROM_SEL, fromStr);
            await SetDate(page, TO_SEL,   toStr);
            await page.WaitForTimeoutAsync(500);

            // Set up listener for AJAX response BEFORE clicking Submit
            // The site makes POST calls to Backpage.aspx — when response arrives, AJAX is done
            var ajaxCompleted = new TaskCompletionSource<bool>();
            EventHandler<IResponse> responseHandler = (_, response) =>
            {
                if (response.Url.Contains("Backpage.aspx") && response.Status == 200)
                {
                    ajaxCompleted.TrySetResult(true);
                }
            };
            page.Response += responseHandler;

            // Click Submit (this triggers the AJAX call)
            await JsClick(page, SUBMIT_SEL);

            // Wait for the data AJAX response (max 15s)
            // Server slow? We wait. Server fast? We proceed immediately.
            try
            {
                await Task.WhenAny(ajaxCompleted.Task, Task.Delay(15_000));
            }
            finally
            {
                page.Response -= responseHandler;  // clean up listener
            }

            // Small pause for DOM to update after response
            await page.WaitForTimeoutAsync(500);

            // Now check: did the CSV link appear? (signals data was found)
            var csvVisible = await page.IsVisibleAsync(CSV_SEL);
            if (!csvVisible)
            {
                Console.WriteLine("Skipped (no data for this period)");
                skipped++;
                if (i < chunks.Count - 1)
                    await page.WaitForTimeoutAsync(DELAY_MS);
                continue;
            }

            // Data is ready — download
            var dlTask = page.WaitForDownloadAsync(new() { Timeout = 15_000 });
            await JsClick(page, CSV_SEL);
            var dl = await dlTask;

            var filename = $"{safeFileName}_{fromStr.Replace("-","")}_{toStr.Replace("-","")}.csv";
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

    // ── Summary for this index ───────────────────────────────────────────────────
    Console.WriteLine();
    Console.WriteLine("════════════════════════════════════");
    Console.WriteLine($"  Index  : {selectedIndexName}");
    Console.WriteLine($"  Saved   : {success}");
    Console.WriteLine($"  Skipped : {skipped}  (no data for period)");
    Console.WriteLine($"  Failed  : {failed}");
    Console.WriteLine($"  Folder  : {downloadsPath}");
    Console.WriteLine("════════════════════════════════════");
    Console.WriteLine();

    // Ask if user wants to download another index
    Console.WriteLine("╔══════════════════════════════════════════════════════╗");
    Console.WriteLine("║  Download another index?                             ║");
    Console.WriteLine("║  Select only another Index and press 'Y' to continue ║");
    Console.WriteLine("║                                                      ║");
    Console.WriteLine("║  Press any other key to EXIT                         ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════╝");
    var key = Console.ReadKey(true);
    if(selectedIndexName == await GetSelectedDropdownText(page, 4) && (key.KeyChar == 'Y' || key.KeyChar == 'y'))
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
        Console.WriteLine(" You have not selected another Index. It is same as before.");
        Console.WriteLine();
        Console.WriteLine(" Please select another Index first and then press any key...");
        Console.WriteLine();
        Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
        Console.ReadKey(true);
    }
    runAnotherCycle = (key.KeyChar == 'Y' || key.KeyChar == 'y');
} // close while loop

// ────────────────────────────────────────────────────────────────────────────
//  HELPERS
// ────────────────────────────────────────────────────────────────────────────

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

static async Task<string> GetSelectedDropdownText(IPage page, int dropdownNumber)
{
    return await page.EvaluateAsync<string>(
        @"(n) => {
            const normalize      = v => (v || '').replace(/\s+/g, ' ').trim();
            const isPlaceholder  = v =>
                !v || /^-+$/.test(v) || /^select\b/i.test(v) || /^please select\b/i.test(v);

            const visibleSelects = Array.from(document.querySelectorAll('select'))
                .filter(s => s.offsetParent !== null);

            const texts = visibleSelects
                .map(s => {
                    const opt = s.options[s.selectedIndex];
                    return normalize(opt?.textContent || s.value);
                })
                .filter(t => !isPlaceholder(t));

            if (texts.length >= n)       return texts[n - 1];
            if (texts.length > 0)        return texts[texts.length - 1];

            const customTexts = Array.from(document.querySelectorAll(
                '.select2-selection__rendered, .chosen-single span, ' +
                '.filter-option-inner-inner, .ui-selectmenu-text'
            ))
                .filter(el => el.offsetParent !== null)
                .map(el => normalize(el.textContent))
                .filter(t => !isPlaceholder(t));

            if (customTexts.length >= n) return customTexts[n - 1];
            if (customTexts.length > 0)  return customTexts[customTexts.length - 1];

            return '(dropdown not found)';
        }",
        dropdownNumber);
}

static string MakeSafeFileName(string value)
{
    var invalid = Path.GetInvalidFileNameChars();
    return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
}