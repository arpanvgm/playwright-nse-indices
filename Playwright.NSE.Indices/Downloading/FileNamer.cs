namespace Playwright.NSE.Indices.Downloading;

public static class FileNamer
{
    /// <summary>
    /// Derives the report suffix token used in filenames and subfolder names.
    /// e.g. "P/E, P/B &amp; Div.Yield" → "PE", everything else → "Price"
    /// </summary>
    public static string GetReportSuffix(string reportName)
        => reportName.Contains("P/E") ? "PE" : "Price";

    /// <summary>
    /// Builds the full output file path.
    /// Pattern: {downloadsPath}/{reportSuffix}/{safeIndexName}_{reportSuffix}_{YYYY}.csv
    /// </summary>
    public static string BuildFilePath(
        string downloadsPath,
        string reportSuffix,
        string safeIndexName,
        DateTime periodEnd)
    {
        var folder   = Path.Combine(downloadsPath, reportSuffix);
        Directory.CreateDirectory(folder);
        var filename = $"{safeIndexName}_{reportSuffix}_{periodEnd:yyyy}.csv";
        return Path.Combine(folder, filename);
    }

    /// <summary>
    /// Replaces characters that are invalid in file names with underscores.
    /// </summary>
    public static string MakeSafe(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
    }
}
