using IndexCsvConsolidator.Models;

namespace IndexCsvConsolidator.Services;

public class FileProcessorService
{
    private readonly AppSettings _settings;
    private readonly LogService _log;
    private readonly InputFileParser _parser;
    private readonly MasterCsvRepository _repository;

    public FileProcessorService(AppSettings settings, LogService log)
    {
        _settings = settings;
        _log = log;
        _parser = new InputFileParser(log);
        _repository = new MasterCsvRepository();
    }

    public void ProcessAll()
    {
        string[] files = Directory.GetFiles(_settings.InputFolder, "*.csv");
        if (files.Length == 0)
        {
            _log.Info("No CSV files found in input folder.");
            _log.Summary("Run complete — Total: 0 | Success: 0 | Failed: 0");
            return;
        }

        int total = files.Length;
        int success = 0;
        int failed = 0;
        _log.Info($"Found {total} file(s) in input folder.");

        foreach (string filePath in files)
        {
            if (ProcessFile(filePath))
                success++;
            else
                failed++;
        }

        _log.Summary($"Run complete — Total: {total} | Success: {success} | Failed: {failed}");
    }

    private bool ProcessFile(string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        int conflictCount = 0;

        try
        {
            List<InputRecord>? inputRecords = _parser.Parse(filePath);
            if (inputRecords == null)
                return false;

            if (inputRecords.Count == 0)
            {
                _log.Warning($"File '{fileName}': No data rows found. Archiving without merge.");
                ArchiveFile(filePath);
                return true;
            }

            string rawIndexName = inputRecords
                .Select(r => r.IndexName)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(rawIndexName))
            {
                _log.Error($"File '{fileName}': No valid IndexName found in any data row. File will not be archived.");
                return false;
            }

            string normalizedIndexName = IndexNameNormalizer.Normalize(rawIndexName);
            string masterPath = Path.Combine(
                _settings.OutputFolder,
                $"Main_{normalizedIndexName}.csv");

            if (!File.Exists(masterPath) && !_settings.AllowCreateMasterIfNotExists)
            {
                _log.Error($"Master file for index '{normalizedIndexName}' not found in output folder. Input file will not be archived.");
                return false;
            }

            List<MasterRecord> masterRecords = _repository.Load(masterPath);
            var masterDict = new Dictionary<string, MasterRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (MasterRecord r in masterRecords)
                masterDict[$"{r.IndexName}|{r.Date}"] = r;

            var seenDatesInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (InputRecord input in inputRecords)
            {
                if (!DateHelper.TryNormalizeDate(input.Date, out string normalizedDate))
                {
                    _log.Error($"File '{fileName}': Cannot parse date '{input.Date}'. Row skipped.");
                    continue;
                }

                if (!seenDatesInFile.Add(normalizedDate))
                {
                    _log.Conflict(
                        $"File '{fileName}' | Date '{normalizedDate}' | Duplicate date within input file — first row kept, this row skipped.");
                    conflictCount++;
                    continue;
                }

                string compositeKey = $"{normalizedIndexName}|{normalizedDate}";
                if (!masterDict.TryGetValue(compositeKey, out MasterRecord? masterRecord))
                {
                    masterRecord = new MasterRecord
                    {
                        IndexName = normalizedIndexName,
                        Date      = normalizedDate,
                    };
                    masterDict[compositeKey] = masterRecord;
                    masterRecords.Add(masterRecord);
                }

                Merge(masterRecord, nameof(MasterRecord.Open), input.Open, fileName, normalizedDate, ref conflictCount);
                Merge(masterRecord, nameof(MasterRecord.High), input.High, fileName, normalizedDate, ref conflictCount);
                Merge(masterRecord, nameof(MasterRecord.Low), input.Low, fileName, normalizedDate, ref conflictCount);
                Merge(masterRecord, nameof(MasterRecord.Close), input.Close, fileName, normalizedDate, ref conflictCount);
                Merge(masterRecord, nameof(MasterRecord.SharesTraded), input.SharesTraded, fileName, normalizedDate, ref conflictCount);
                Merge(masterRecord, nameof(MasterRecord.TurnoverInrCr), input.TurnoverInrCr, fileName, normalizedDate, ref conflictCount);
                Merge(masterRecord, nameof(MasterRecord.PE), input.PE, fileName, normalizedDate, ref conflictCount);
                Merge(masterRecord, nameof(MasterRecord.PB), input.PB, fileName, normalizedDate, ref conflictCount);
                Merge(masterRecord, nameof(MasterRecord.DividendYield), input.DividendYield, fileName, normalizedDate, ref conflictCount);
            }

            masterRecords.Sort((a, b) =>
            {
                DateHelper.TryParseOutputDate(a.Date, out DateTime da);
                DateHelper.TryParseOutputDate(b.Date, out DateTime db);
                return da.CompareTo(db);
            });

            Directory.CreateDirectory(_settings.OutputFolder);
            _repository.Save(masterPath, masterRecords);

            ArchiveFile(filePath);

            _log.Info(
                $"File '{fileName}' → '{Path.GetFileName(masterPath)}' | Master rows: {masterRecords.Count} | Conflicts: {conflictCount}");

            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"File '{fileName}': Unexpected error — {ex.Message}");
            return false;
        }
    }

    private void Merge(
        MasterRecord masterRecord,
        string propertyName,
        string incomingValue,
        string fileName,
        string date,
        ref int conflictCount)
    {
        if (string.IsNullOrWhiteSpace(incomingValue))
            return;

        var prop = typeof(MasterRecord).GetProperty(propertyName)
            ?? throw new InvalidOperationException(
                $"Property '{propertyName}' does not exist on MasterRecord.");

        string existingValue = (string)(prop.GetValue(masterRecord) ?? string.Empty);

        if (string.IsNullOrWhiteSpace(existingValue))
        {
            prop.SetValue(masterRecord, incomingValue);
            return;
        }

        if (existingValue.Equals(incomingValue, StringComparison.OrdinalIgnoreCase))
            return;

        conflictCount++;

        if (_settings.OverwriteExistingValue)
        {
            _log.Conflict(
                $"OVERWRITE | File: '{fileName}' | Date: '{date}' | Field: '{propertyName}' | Was: '{existingValue}' | Now: '{incomingValue}'");
            prop.SetValue(masterRecord, incomingValue);
        }
        else
        {
            _log.Conflict(
                $"KEPT      | File: '{fileName}' | Date: '{date}' | Field: '{propertyName}' | Kept: '{existingValue}' | Ignored: '{incomingValue}'");
        }
    }

    private void ArchiveFile(string filePath)
    {
        string fileName = Path.GetFileName(filePath);

        string subFolder;
        if (fileName.Contains("_PE_", StringComparison.OrdinalIgnoreCase))
            subFolder = "peData";
        else if (fileName.Contains("_Price_", StringComparison.OrdinalIgnoreCase))
            subFolder = "priceData";
        else
        {
            _log.Warning($"File '{fileName}': Could not determine archive subfolder from filename. Archiving to root.");
            subFolder = string.Empty;
        }

        string archiveFolder = string.IsNullOrEmpty(subFolder)
            ? _settings.ArchiveFolder
            : Path.Combine(_settings.ArchiveFolder, subFolder);

        Directory.CreateDirectory(archiveFolder);
        string destination = Path.Combine(archiveFolder, fileName);

        try
        {
            File.Copy(filePath, destination, overwrite: true);
            
            if (File.Exists(destination))
            {
                File.Delete(filePath);
                _log.Info($"Successfully archived and removed original file: {fileName}");
            }
            else
            {
                _log.Error($"File '{fileName}': Copy completed but destination file not found. Original file kept.");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"File '{fileName}': Archive failed — {ex.Message}. Original file kept.");
        }
    }
}
