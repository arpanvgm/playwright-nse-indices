using System.Globalization;

namespace IndexCsvConsolidator.Services;

public static class DateHelper
{
    private static readonly string[] InputFormats =
    {
        "dd MMM yyyy",
        "d MMM yyyy",
        "dd-MMM-yyyy",
        "d-MMM-yyyy"
    };

    private static readonly string[] OutputParseFormats =
    {
        "dd-MMM-yyyy",
        "d-MMM-yyyy"
    };

    private const string OutputFormat = "dd-MMM-yyyy";

    public static bool TryNormalizeDate(string rawDate, out string normalizedDate)
    {
        normalizedDate = string.Empty;

        if (string.IsNullOrWhiteSpace(rawDate))
            return false;

        if (DateTime.TryParseExact(
                rawDate.Trim(),
                InputFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime dt))
        {
            normalizedDate = dt.ToString(OutputFormat, CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    public static bool TryParseOutputDate(string date, out DateTime result)
    {
        return DateTime.TryParseExact(
            date.Trim(),
            OutputParseFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);
    }
}
