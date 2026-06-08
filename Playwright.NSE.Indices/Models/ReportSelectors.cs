namespace Playwright.NSE.Indices.Models;

public record ReportSelectors(
    string Name,
    string SectionSelector,
    string IndexTypeDropdown,
    string SubIndexDropdown,
    string IndexNameDropdown,
    string From,
    string To,
    string Submit,
    string Csv,
    string AjaxUrlFragment);
