using CsvHelper;
using CsvHelper.Configuration;
using IndexCsvConsolidator.Models;
using System.Globalization;

namespace IndexCsvConsolidator.Services;

public class InputFileParser
{
    private readonly LogService _log;

    public InputFileParser(LogService log)
    {
        _log = log;
    }

    public List<InputRecord>? Parse(string filePath)
    {
        string fileName = Path.GetFileName(filePath);

        try
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                TrimOptions = TrimOptions.Trim,
                MissingFieldFound = null,
                HeaderValidated = null,
            };

            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, config);

            csv.Read();
            csv.ReadHeader();

            var rawHeaders = csv.HeaderRecord
                ?? throw new InvalidDataException("File has no header row.");

            var headers = rawHeaders
                .Select(h => h?.Trim() ?? string.Empty)
                .ToList();

            bool hasDate = headers.Any(h => h.Equals("Date", StringComparison.OrdinalIgnoreCase));
            bool hasIndex = headers.Any(h =>
                h.Equals("IndexName", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("Index Name", StringComparison.OrdinalIgnoreCase));

            if (!hasDate)
            {
                _log.Error($"File '{fileName}': Missing required 'Date' column. File will not be archived.");
                return null;
            }

            if (!hasIndex)
            {
                _log.Error($"File '{fileName}': Missing required 'IndexName' or 'Index Name' column. File will not be archived.");
                return null;
            }

            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(headers[i]) && !headerMap.ContainsKey(headers[i]))
                    headerMap[headers[i]] = i;
            }

            var records = new List<InputRecord>();

            while (csv.Read())
            {
                string[]? row = csv.Parser.Record;
                if (row == null) continue;

                string GetVal(params string[] names)
                {
                    foreach (string name in names)
                    {
                        if (headerMap.TryGetValue(name, out int idx) && idx < row.Length)
                            return row[idx]?.Trim() ?? string.Empty;
                    }
                    return string.Empty;
                }

                var record = new InputRecord
                {
                    IndexName = GetVal("IndexName", "Index Name"),
                    Date = GetVal("Date"),
                    Open = GetVal("Open"),
                    High = GetVal("High"),
                    Low = GetVal("Low"),
                    Close = GetVal("Close"),
                    SharesTraded = GetVal("SharesTraded"),
                    TurnoverInrCr = GetVal("TurnoverInrCr"),
                    PE = GetVal("P/E"),
                    PB = GetVal("P/B"),
                    DividendYield = GetVal("Div Yield %"),
                };

                if (string.IsNullOrWhiteSpace(record.IndexName) &&
                    string.IsNullOrWhiteSpace(record.Date))
                {
                    continue;
                }

                records.Add(record);
            }

            return records;
        }
        catch (Exception ex)
        {
            _log.Error($"File '{fileName}': Parse error — {ex.Message}. File will not be archived.");
            return null;
        }
    }
}
