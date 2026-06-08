using Microsoft.Playwright;
using Playwright.NSE.Indices.Browser;
using Playwright.NSE.Indices.Models;

namespace Playwright.NSE.Indices.Downloading;

public record NavigationResult(bool Success, string FailureReason = "");

public static class StartupNavigator
{
    private const int AjaxTimeoutMs  = 15_000;
    private const int SettleDelayMs  = 1_000;
    private const string IndexType   = "Equity";

    /// <summary>
    /// Fully automates the two manual steps that were previously done by hand:
    ///   Step 1 — Click the report section header (li.form1 / li.form4)
    ///            (No longer waits for AJAX, as the site made this a client-side switch)
    ///   Step 2 — Select "Equity" in the Index Type dropdown
    ///            and wait for the AJAX response that loads the Sub-Index dropdown.
    ///
    /// Returns NavigationResult(false, reason) if AJAX times out.
    /// The caller must abort the entire run on failure — there is no point continuing.
    /// </summary>
    public static async Task<NavigationResult> PrepareReportAsync(
        IPage page,
        ReportSelectors report)
    {
        // ── Step 1: click the section header ────────────────────────────────
        Console.Write($"  Clicking section '{report.Name}' ... ");

        await PageInteractions.JsClickAsync(page, report.SectionSelector);

        // The site changed its architecture; tab switching is now purely client-side
        // and no longer fires a Backpage.aspx POST request. We just wait for the UI to settle.
        await page.WaitForTimeoutAsync(SettleDelayMs);
        
        Console.WriteLine("done.");

        // ── Step 2: select "Equity" in the Index Type dropdown ───────────────
        Console.Write($"  Selecting Index Type = '{IndexType}' ... ");

        var step2Ajax = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<IResponse> step2Handler = (_, response) =>
        {
            if (response.Url.Contains(report.AjaxUrlFragment) && response.Status == 200)
                step2Ajax.TrySetResult(true);
        };
        page.Response += step2Handler;

        var selected = await PageInteractions.SelectDropdownByTextAsync(
            page, IndexType, report.IndexTypeDropdown);

        if (!selected)
        {
            page.Response -= step2Handler;
            var reason = $"Could not select '{IndexType}' in dropdown '{report.IndexTypeDropdown}'";
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"FAILED — {reason}");
            Console.ResetColor();
            return new NavigationResult(false, reason);
        }

        var step2Winner = await Task.WhenAny(step2Ajax.Task, Task.Delay(AjaxTimeoutMs));
        page.Response -= step2Handler;

        if (step2Winner != step2Ajax.Task)
        {
            var reason = $"AJAX timeout after selecting '{IndexType}' for report '{report.Name}'";
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"FAILED — {reason}");
            Console.ResetColor();
            return new NavigationResult(false, reason);
        }

        Console.WriteLine("done.");
        await page.WaitForTimeoutAsync(SettleDelayMs);

        return new NavigationResult(true);
    }
}
