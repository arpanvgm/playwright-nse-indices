using Microsoft.Playwright;
using Playwright.NSE.Indices.Browser;
using Playwright.NSE.Indices.Logging;
using Playwright.NSE.Indices.Models;

namespace Playwright.NSE.Indices.Downloading;

public class DownloadOrchestrator
{
    private readonly IPage                               _page;
    private readonly AppLogger                          _logger;
    private readonly string                             _downloadsPath;
    private readonly int                                _delayMs;
    private readonly ReportSelectors                    _activeSelectors;
    private readonly string                             _activeSubIndexSelector;
    private readonly bool                               _hasSubIndexDropdown;
    private readonly List<string>                       _subIndexUIOptions;
    private readonly List<IndexConfig>                  _indexNames;
    private readonly List<(DateTime From, DateTime To)> _chunks;
    private readonly string                             _ajaxUrlFragment;

    public int  TotalSuccess               { get; private set; }
    public int  TotalSkipped               { get; private set; }
    public int  TotalFailed                { get; private set; }
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
        _ajaxUrlFragment        = activeSelectors.AjaxUrlFragment;
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

                Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine($"  CATEGORY: {currentCategory}  ({targetIndices.Count} indices)");
                Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.Write($"  Changing UI to '{currentCategory}' ... ");

                var ajaxCompleted = new TaskCompletionSource<bool>();
                EventHandler<IResponse> responseHandler = (_, response) =>
                {
                    if (response.Url.Contains(_ajaxUrlFragment) && response.Status == 200)
                        ajaxCompleted.TrySetResult(true);
                };
                _page.Response += responseHandler;

                var selectedCategory = await PageInteractions.SelectDropdownByTextAsync(
                    _page, currentCategory, _activeSubIndexSelector);

                if (!selectedCategory)
                {
                    Console.WriteLine("FAILED — could not select category in UI. Skipping.");
                    _logger.Error($"Category '{currentCategory}': could not select in UI");
                    _page.Response -= responseHandler;
                    Console.WriteLine();
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
            }
            else
            {
                targetIndices = _indexNames;
                Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Console.WriteLine($"  PROCESSING ALL INDICES  ({targetIndices.Count})");
                Console.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            }

            await ProcessIndicesAsync(targetIndices);

            if (ServerUnresponsiveDetected)
                break;

            Console.WriteLine();
        }
    }

    // ── Private: per-index loop ───────────────────────────────────────────────

    private async Task ProcessIndicesAsync(List<IndexConfig> targetIndices)
    {
        for (int idx = 0; idx < targetIndices.Count; idx++)
        {
            var indexName = targetIndices[idx].Name;

            var selected = await PageInteractions.SelectDropdownByTextAsync(
                _page, indexName, _activeSelectors.IndexNameDropdown);

            if (!selected)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Fail  | UI Selection | {indexName}");
                Console.ResetColor();

                _logger.Error($"Fail  | UI Selection | {indexName}");
                continue;
            }

            await _page.WaitForTimeoutAsync(600);

            await ProcessChunksAsync(indexName);

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

            try
            {
                await PageInteractions.SetDateAsync(_page, _activeSelectors.From, fromStr);
                await PageInteractions.SetDateAsync(_page, _activeSelectors.To,   toStr);
                await _page.WaitForTimeoutAsync(500);

                var ajaxCompleted = new TaskCompletionSource<bool>();
                EventHandler<IResponse> responseHandler = (_, response) =>
                {
                    if (response.Url.Contains(_ajaxUrlFragment) && response.Status == 200)
                        ajaxCompleted.TrySetResult(true);
                };
                _page.Response += responseHandler;

                await PageInteractions.JsClickAsync(_page, _activeSelectors.Submit);

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

                // ── Timeout: server did not respond ───────────────────────────
                if (winner == timeoutTask)
                {
                    var msg = $"Fail  | AJAX Timeout  | {fromStr} to {toStr} | {indexName} | server unresponsive";
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(msg);
                    Console.ResetColor();
                    _logger.Warn(msg);

                    ServerUnresponsiveDetected = true;
                    TotalFailed  += failed;
                    TotalSkipped += skipped;
                    TotalSuccess += success;
                    return;
                }

                // ── AJAX completed: check if data was returned ────────────────
                await _page.WaitForTimeoutAsync(750);
                var csvVisible = await _page.IsVisibleAsync(_activeSelectors.Csv);

                if (!csvVisible)
                {
                    Console.WriteLine($"Skip  | {fromStr} to {toStr} | {indexName}");
                    _logger.Info($"Skip  | {fromStr} to {toStr} | {indexName}");
                    skipped++;
                    if (i < _chunks.Count - 1)
                        await _page.WaitForTimeoutAsync(_delayMs);
                    continue;
                }

                // ── Data available: download ──────────────────────────────────
                var dlTask = _page.WaitForDownloadAsync(new() { Timeout = 15_000 });
                await PageInteractions.JsClickAsync(_page, _activeSelectors.Csv);
                var dl = await dlTask;

                var filePath = FileNamer.BuildFilePath(_downloadsPath, reportSuffix, safeFileName, to);
                var fileName = Path.GetFileName(filePath);

                await dl.SaveAsAsync(filePath);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Saved | {fromStr} to {toStr} | {fileName}");
                Console.ResetColor();
                _logger.Info($"Saved | {fromStr} to {toStr} | {fileName}");
                success++;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Fail  | Exception    | {fromStr} to {toStr} | {indexName} | {ex.Message}");
                Console.ResetColor();
                _logger.Error($"Fail  | {fromStr} to {toStr} | {indexName}", ex);
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
