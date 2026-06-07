using Microsoft.Playwright;
using Playwright.NSE.Indices.Models;
using System.Text.Json;

namespace Playwright.NSE.Indices.Downloading;

public record SubIndexDetectionResult(string ActiveSelector, List<string> Options, bool Found);

public static class ReportDetector
{
    /// <summary>
    /// Scans the page for the first visible date-from input that matches
    /// a known report type. Returns the matching ReportSelectors or null.
    /// </summary>
    public static async Task<ReportSelectors?> DetectActiveReportAsync(
        IPage page,
        ReportSelectors[] knownReports)
    {
        foreach (var report in knownReports)
        {
            var isVisible = await page.EvaluateAsync<bool>($@"() => {{
                const el = document.querySelector('{report.From}');
                return el && el.offsetParent !== null;
            }}");

            if (isVisible)
                return report;
        }

        return null;
    }

    /// <summary>
    /// Scans all visible dropdowns to find the one whose options match
    /// any of the known sub-index category names from indices.json.
    /// Falls back to the explicit selector in activeSelectors if not found.
    /// Returns a SubIndexDetectionResult with the resolved selector, UI options,
    /// and whether a sub-index dropdown was found at all.
    /// </summary>
    public static async Task<SubIndexDetectionResult> DetectSubIndexDropdownAsync(
        IPage page,
        ReportSelectors activeSelectors,
        List<string> uniqueSubIndices)
    {
        // Try smart data-match scan first
        var detection = await page.EvaluateAsync<JsonElement>($@"(subs) => {{
            const normalize = v => (v || '').replace(/\s+/g, ' ').trim().toLowerCase();
            const targetSubs = subs.map(normalize);

            const selects = Array.from(document.querySelectorAll('select'))
                .filter(s => s.offsetParent !== null);

            for (let i = 0; i < selects.length; i++) {{
                const select = selects[i];
                const optionTexts = Array.from(select.options).map(o => o.textContent.trim());
                const normalizedOptions = optionTexts.map(normalize);

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

        bool found = detection.TryGetProperty("found", out var f) && f.GetBoolean();
        var options = new List<string>();
        string activeSelector = activeSelectors.SubIndexDropdown; // fallback

        if (found)
        {
            var selElement = detection.GetProperty("selector");
            if (selElement.ValueKind == JsonValueKind.String)
                activeSelector = selElement.GetString()!;

            foreach (var opt in detection.GetProperty("options").EnumerateArray())
                options.Add(opt.GetString()!);

            return new SubIndexDetectionResult(activeSelector, options, true);
        }

        // Fallback: use the known explicit selector
        var fallbackFound = await page.EvaluateAsync<bool>($@"() => {{
            const el = document.querySelector('{activeSelectors.SubIndexDropdown}');
            return el && el.offsetParent !== null;
        }}");

        if (fallbackFound)
        {
            var fallbackOptions = await page.EvaluateAsync<List<string>>($@"(sel) => {{
                const select = document.querySelector(sel);
                return Array.from(select.options)
                    .filter(o => o.value && o.value !== '0' && !o.text.toLowerCase().includes('select'))
                    .map(o => o.textContent.trim());
            }}", activeSelectors.SubIndexDropdown);

            return new SubIndexDetectionResult(activeSelectors.SubIndexDropdown, fallbackOptions, true);
        }

        // No sub-index dropdown found — flat mode
        return new SubIndexDetectionResult(
            activeSelectors.SubIndexDropdown,
            new List<string> { "DEFAULT_FLAT_MODE" },
            false);
    }
}
