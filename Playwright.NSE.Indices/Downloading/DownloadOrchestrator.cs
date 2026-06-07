using Microsoft.Playwright;
using Playwright.NSE.Indices.Browser;
using Playwright.NSE.Indices.Logging;
using Playwright.NSE.Indices.Models;

namespace Playwright.NSE.Indices.Downloading;

public class DownloadOrchestrator
{
    private readonly IPage                              _page;
    private readonly AppLogger                         _logger;
    private readonly string                            _downloadsPath;
    private readonly int                               _delayMs;
    private readonly ReportSelectors                   _activeSelectors;
    private readonly string                            _activeSubIndexSelector;
    private readonly bool                              _hasSubIndexDropdown;
    private readonly List<string>                      _subIndexUIOptions;
    private readonly List<IndexConfig>                 _indexNames;
    private readonly List<(DateTime From, DateTime To)> _chunks;

    public int  TotalSuccess  { get; private set; }
    public int  TotalSkipped  { get; private set; }
    public int  TotalFailed   { get; private set; }

    /// <summary>
    /// Set to true when an AJAX timeout is detected mid-run.
    /// Signals that the server is unresponsive — caller can check this after RunAsync returns.
    /// </summary>
    public bool ServerUnresponsiveDetected { get; private set; }

    public DownloadOrchestrator(
        IPage page,
        AppLogger logger,
        string downloadsPath,
        int delayMs,
        ReportSelectors activeSelectors,
        string activeSubIndexSelector,
        bool hasSubIndexDropdown,
        List<string> subIndexUIOptions,
        List<IndexConfig> indexNames,
        List<(DateTime From, DateTime To)> chunks)
    {
        _page                   = page;
        _logger                 = logger;
        _downloadsPath          = downloadsPath;
        _delayMs                = delayMs;
        _activeSelectors        = activeSelectors;
        _activeSubIndexSelector = activeSubIndexSelector;
        _hasSubIndexDropdown    = hasSubIndexDropdown;
        _subIndexUIOptions      = subIndexUIOptions;
        _indexNames             = indexNames;
        _chunks                 = chunks;
    }

    public async Task RunAsync()
    {
        foreach (var currentCategory in _subIndexUIOptions)
        {
            List<IndexConfig> targetIndices;

            if (_hasSubIndexDropdown)
            {
                targetIndices = _indexNames
                    .Where(x => string.Equals(x.SubIndex, currentCategory, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (targetIndices.Count == 0)
                    continue;

                var catHeader = $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                $"  CATEGORY: {currentCategory}  ({targetIndices.Count} indices)\n" +
                                $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━";
                Console.WriteLine(catHeader);
                _logger.LogOnly(catHeader);

                Console.Write($"  Changing UI to '{currentCategory}' ... ");
                _logger.LogOnly($"  Changing UI to '{currentCategory}' ...");

                var ajaxCompleted = new TaskCompletionSource<bool>();
                EventHandler<IResponse> responseHandler = (_, response) =>
                {
                    if (response.Url.Contains("Backpage.aspx") && response.Status == 200)
                        ajaxCompleted.TrySetResult(true);
                };
                _page.Response += responseHandler;

                var selectedCategory = await PageInteractions.SelectDropdownByTextAsync(
                    _page, currentCategory, _activeSubIndexSelector);

                if (!selectedCategory)
                {
                    var failMsg = "FAILED — could not select category in UI. Skipping.";
                    Console.WriteLine(failMsg);
                    _logger.Error($"  CATEGORY '{currentCategory}': {failMsg}");
                    _page.Response -= responseHandler;
                    Console.WriteLine();
                    _logger.Separator();
                    continue;
                }

                try
                {
                    await Task.WhenAny(ajaxCompleted.Task, Task.Delay(10_000));
                }
                finally
                {
                    _page.Response -= responseHandler;
                }

                await _page.WaitForTimeoutAsync(1000);
                Console.WriteLine("done.\n");
                _logger.LogOnly($"  Category '{currentCategory}' selected successfully.");
            }
            else
            {
                targetIndices = _indexNames;
                var flatHeader = $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                                 $"  PROCESSING ALL INDICES  ({targetIndices.Count})\n" +
                                 $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━";
                Console.WriteLine(flatHeader);
                _logger.LogOnly(flatHeader);
            }

            await ProcessIndicesAsync(targetIndices);

            // Server unresponsive detected inside the chunk loop — stop all category loops
            if (ServerUnresponsiveDetected)
                break;

            Console.WriteLine();
            _logger.Separator();
        }
    }

    // ── Private: per-index loop ───────────────────────────────────────────────

    private async Task ProcessIndicesAsync(List<IndexConfig> targetIndices)
    {
        for (int idx = 0; idx < targetIndices.Count; idx++)
        {
            var indexName = targetIndices[idx].Name;
            Console.Write($"  [{idx + 1}/{targetIndices.Count}]  {indexName}  -  Selecting '{indexName}' ... ");
            _logger.LogOnly($"  [{idx + 1}/{targetIndices.Count}]  {indexName}  - Selecting in UI...");

            var selected = await PageInteractions.SelectDropdownByTextAsync(_page, indexName, null);
            if (!selected)
            {
                var skipMsg = "FAILED — option not found. Skipping.";
                Console.WriteLine(skipMsg);
                _logger.Error($"  Index '{indexName}': {skipMsg}");
                continue;
            }

            await _page.WaitForTimeoutAsync(600);
            Console.WriteLine("done.");
            _logger.LogOnly($"  Index '{indexName}': selected successfully.");

            await ProcessChunksAsync(indexName);

            // Propagate stop signal up to the category loop
            if (ServerUnresponsiveDetected)
                break;
        }
    }

    // ── Private: per-chunk loop ───────────────────────────────────────────────

    private async Task ProcessChunksAsync(string indexName)
    {
        int success = 0, skipped = 0, failed = 0;
        var safeFileName = FileNamer.MakeSafe(indexName);
        var reportSuffix = FileNamer.GetReportSuffix(_activeSelectors.Name);

        for (int i = 0; i < _chunks.Count; i++)
        {
            var (from, to) = _chunks[i];
            var fromStr    = from.ToString("dd-MM-yyyy");
            var toStr      = to.ToString("dd-MM-yyyy");

            Console.Write($"    [{i + 1}/{_chunks.Count}] {fromStr} to {toStr} ... ");
            _logger.LogOnly($"    [{i + 1}/{_chunks.Count}] {indexName} | {fromStr} to {toStr}");

            try
            {
                await PageInteractions.SetDateAsync(_page, _activeSelectors.From, fromStr);
                await PageInteractions.SetDateAsync(_page, _activeSelectors.To,   toStr);
                await _page.WaitForTimeoutAsync(500);

                var ajaxCompleted = new TaskCompletionSource<bool>();
                EventHandler<IResponse> responseHandler = (_, response) =>
                {
                    if (response.Url.Contains("Backpage.aspx") && response.Status == 200)
                        ajaxCompleted.TrySetResult(true);
                };
                _page.Response += responseHandler;

                await PageInteractions.JsClickAsync(_page, _activeSelectors.Submit);

                // ── Race: AJAX response vs timeout ────────────────────────────
                var timeoutTask = Task.Delay(15_000);
                Task winner;
                try
                {
                    winner = await Task.WhenAny(ajaxCompleted.Task, timeoutTask);
                }
                finally
                {
                    _page.Response -= responseHandler;
                }

                // ── Timeout path: server did not respond ──────────────────────
                if (winner == timeoutTask)
                {
                    var msg = $"AJAX timeout — server unresponsive. Stopping run. " +
                              $"({indexName} | {fromStr} to {toStr})";
                    Console.WriteLine();
                    Console.WriteLine($"⚠  {msg}");
                    _logger.Warn(msg);

                    ServerUnresponsiveDetected = true;
                    TotalFailed += failed;
                    TotalSkipped += skipped;
                    TotalSuccess += success;
                    return;
                }

                // ── AJAX completed path: check if data was returned ───────────
                await _page.WaitForTimeoutAsync(500);
                var csvVisible = await _page.IsVisibleAsync(_activeSelectors.Csv);

                if (!csvVisible)
                {
                    Console.WriteLine("Skipped (no data for this period)");
                    _logger.LogOnly($"    Skipped — no data: {indexName} | {fromStr} to {toStr}");
                    skipped++;
                    if (i < _chunks.Count - 1)
                        await _page.WaitForTimeoutAsync(_delayMs);
                    continue;
                }

                // ── Data available: download ──────────────────────────────────
                var dlTask = _page.WaitForDownloadAsync(new() { Timeout = 15_000 });
                await PageInteractions.JsClickAsync(_page, _activeSelectors.Csv);
                var dl = await dlTask;

                var filePath  = FileNamer.BuildFilePath(_downloadsPath, reportSuffix, safeFileName, to);
                var fileLabel = $"{reportSuffix}\\{Path.GetFileName(filePath)}";

                await dl.SaveAsAsync(filePath);
                Console.WriteLine($"Saved: {fileLabel}");
                _logger.LogOnly($"    Saved: {filePath}");
                success++;
            }
            catch (Exception ex)
            {
                var errMsg = $"FAILED: {ex.Message}";
                Console.WriteLine(errMsg);
                _logger.Error($"    FAILED: {indexName} | {fromStr} to {toStr}", ex);
                failed++;
                await _page.WaitForTimeoutAsync(_delayMs * 2);
                continue;
            }

            if (i < _chunks.Count - 1)
                await _page.WaitForTimeoutAsync(_delayMs);
        }

        TotalSuccess += success;
        TotalSkipped += skipped;
        TotalFailed  += failed;
    }
}
