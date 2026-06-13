namespace Playwright.NSE.Indices.Downloading;

public static class FileNamer
{
    /// <summary>
    /// Derives the report suffix token used in filenames.
    /// e.g. "P/E, P/B &amp; Div.Yield" → "PE", everything else → "Price"
    /// </summary>
    public static string GetReportSuffix(string reportName)
        => reportName.Contains("P/E") ? "PE" : "Price";

    /// <summary>
    /// Builds the full output file path.
    /// All files are saved flat into downloadsPath with no subfolders.
    /// Pattern: {downloadsPath}/{safeIndexName}_{reportSuffix}_{YYYY}.csv
    /// </summary>
    public static string BuildFilePath(
        string downloadsPath,
        string reportSuffix,
        string safeIndexName,
        DateTime periodEnd)
    {
        Directory.CreateDirectory(downloadsPath);
        var filename = $"{safeIndexName}_{reportSuffix}_{periodEnd:yyyy}.csv";
        return Path.Combine(downloadsPath, filename);
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
