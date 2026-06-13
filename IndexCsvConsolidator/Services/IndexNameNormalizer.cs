using System.Text.RegularExpressions;

namespace IndexCsvConsolidator.Services;

public static class IndexNameNormalizer
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    public static string Normalize(string indexName)
    {
        if (string.IsNullOrWhiteSpace(indexName))
            throw new ArgumentException("Index name cannot be null or whitespace.", nameof(indexName));

        string result = indexName.Trim();
        result = Regex.Replace(result, @"\s+", "-");

        foreach (char c in InvalidFileNameChars)
            result = result.Replace(c.ToString(), string.Empty);

        return result;
    }
}
