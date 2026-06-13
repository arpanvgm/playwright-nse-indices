# Index CSV Consolidator — Implementation Plan & README

> **For AI Coding Agents:** Follow every step in order. Every file path, class name, property name, and code block is exact and complete. Do not deviate. Do not add extra abstractions. Create every file as specified.

---

---

# PART 1 — README (How to Operate)

---

## What This Program Does

Reads CSV files from an input folder, merges them into per-index master CSV files in an output folder, and moves successfully processed files to an archive folder. One master file per index is maintained and sorted by ascending date at all times.

---

## Prerequisites

- Windows OS (x64)
- .NET 10 SDK installed — download from https://dotnet.microsoft.com/download/dotnet/10.0
- To verify: open a command prompt and run `dotnet --version` — must show `10.x.x`

---

## Folder Structure After Build

```
IndexCsvConsolidator/          ← solution root
├── IndexCsvConsolidator.sln
└── IndexCsvConsolidator/      ← project root
    ├── appsettings.json       ← EDIT THIS to configure folders
    ├── Models/
    ├── Services/
    ├── Program.cs
    └── ...

publish/                       ← produced after publish command
└── IndexCsvConsolidator.exe   ← run this file
    appsettings.json           ← copy here and edit before running
    Logs/                      ← log files created here automatically
```

---

## Configuration

Edit `appsettings.json` before running. All three folder paths must exist on disk (Output and Archive are created automatically; Input must exist).

```json
{
  "Settings": {
    "InputFolder": "D:\\Data\\Input",
    "OutputFolder": "D:\\Data\\Output",
    "ArchiveFolder": "D:\\Data\\Archive",
    "OverwriteExistingValue": false,
    "AllowCreateMasterIfNotExists": false
  }
}
```

| Key | Description |
|---|---|
| `InputFolder` | Folder where incoming CSV files are placed |
| `OutputFolder` | Folder where master CSV files are written |
| `ArchiveFolder` | Folder where successfully processed input files are moved |
| `OverwriteExistingValue` | `false` = keep existing value when conflict occurs (recommended). `true` = overwrite existing with incoming value. Either way, all conflicts are logged. |
| `AllowCreateMasterIfNotExists` | `false` = master file must already exist in output folder; if missing, input file is skipped and logged as error (default). `true` = create master file automatically if it does not exist. |

Use double backslashes `\\` in paths on Windows.

---

## How to Build

Open a terminal in the solution root folder (where `IndexCsvConsolidator.sln` is located) and run:

```bash
dotnet build
```

---

## How to Publish (Single EXE, No Runtime Required)

```bash
dotnet publish IndexCsvConsolidator/IndexCsvConsolidator.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The `publish` folder will contain `IndexCsvConsolidator.exe`.

Copy `appsettings.json` from the project folder into the `publish` folder alongside the exe, then edit it with correct folder paths.

---

## How to Run

1. Place input CSV files in the configured `InputFolder`.
2. Open a terminal in the folder containing `IndexCsvConsolidator.exe`.
3. Run:

```bash
IndexCsvConsolidator.exe
```

Or from any location:

```bash
C:\path\to\publish\IndexCsvConsolidator.exe
```

The program exits automatically when all files are processed.

---

## How to Schedule (Windows Task Scheduler)

1. Open Task Scheduler → Create Basic Task.
2. Set trigger (e.g., Daily at a specific time).
3. Action: Start a program → browse to `IndexCsvConsolidator.exe`.
4. Set "Start in" to the folder containing the exe so `appsettings.json` is found.

---

## Input File Rules

- Must be `.csv` files.
- Must have a header row.
- Must contain a `Date` column.
- Must contain an `IndexName` or `Index Name` column.
- One file must contain data for only one index.
- File name does not matter.

**Price input example:**
```csv
Index Name,Date,Open,High,Low,Close
NIFTY PRIVATE BANK,24 Dec 2025,28531.00,28614.15,28429.05,28460.80
```

**Valuation input example:**
```csv
IndexName,Date,P/E,P/B,Div Yield %
NIFTY PRIVATE BANK,24 Dec 2025,20.05,2.21,0.53
```

Both types can be processed together in one run. The same index can receive both price and valuation inputs; they will be merged into one master row per date.

---

## Output Files

Master files are written to `OutputFolder` with naming:

```
Main_{NormalizedIndexName}.csv
```

Example: `Main_NIFTY-PRIVATE-BANK.csv`

Output columns (always in this order):

```
IndexName,Date,Open,High,Low,Close,SharesTraded,TurnoverInrCr,PE,PB,DividendYield
```

Empty fields are written as empty (no value between commas). Dates are formatted as `24-Dec-25`.

---

## Logs

A `Logs` folder is created automatically next to the exe. Each run creates one log file named `run_YYYYMMDD_HHmmss.log`.

Log levels:

| Level | Meaning |
|---|---|
| `INFO` | Normal progress — file started, file completed, run summary |
| `WARN` | Non-fatal issue — e.g. empty input file |
| `ERROR` | File failed to process — file remains in Input folder |
| `CONF` | Value conflict detected — what was kept / overwritten |
| `SUMM` | Run summary at end — total / success / failed counts |

---

## Conflict Behaviour

A conflict occurs when an incoming non-empty value differs from an existing non-empty value for the same date and field.

- If `OverwriteExistingValue = false`: existing value is kept, incoming is ignored. Conflict is logged.
- If `OverwriteExistingValue = true`: existing value is overwritten with incoming. Conflict is logged.

Duplicate dates within the same input file: first row is kept, duplicate rows are skipped and logged as conflicts.

---

## Troubleshooting

| Symptom | Check |
|---|---|
| Program exits with "Input folder not found" | Verify `InputFolder` path in `appsettings.json` exists |
| File stays in Input folder after run | Check log file for `ERROR` lines for that file name |
| Master CSV has wrong column order | Ensure you have not manually edited the master CSV headers |
| `appsettings.json` not found error | Ensure `appsettings.json` is in the same folder as the exe |
| Date not parsed | Input date format must be `DD Mon YYYY` (e.g. `24 Dec 2025`) |

---

---

# PART 2 — IMPLEMENTATION PLAN

---

## Technology Stack

| Item | Choice |
|---|---|
| Language | C# 12 |
| Framework | .NET 10 |
| Output | Console application — single self-contained exe |
| CSV library | CsvHelper 33.0.1 (NuGet) |
| Configuration | Microsoft.Extensions.Configuration + JSON provider |
| Logging | Custom (console + file) — no third-party logging library |
| Target OS | Windows x64 |

---

## Project Layout

```
IndexCsvConsolidator/                    ← solution root directory
├── IndexCsvConsolidator.sln
└── IndexCsvConsolidator/                ← project directory
    ├── IndexCsvConsolidator.csproj
    ├── appsettings.json
    ├── Program.cs
    ├── Models/
    │   ├── AppSettings.cs
    │   ├── InputRecord.cs
    │   └── MasterRecord.cs
    └── Services/
        ├── DateHelper.cs
        ├── FileProcessorService.cs
        ├── IndexNameNormalizer.cs
        ├── InputFileParser.cs
        ├── LogService.cs
        └── MasterCsvRepository.cs
```

---

## Step 1 — Create Solution and Project

Run these commands from an empty working directory. This becomes the solution root.

```bash
dotnet new sln -n IndexCsvConsolidator
dotnet new console -n IndexCsvConsolidator -f net10.0 -o IndexCsvConsolidator
dotnet sln add IndexCsvConsolidator/IndexCsvConsolidator.csproj
```

Then create the subdirectories inside `IndexCsvConsolidator/`:

```bash
mkdir IndexCsvConsolidator/Models
mkdir IndexCsvConsolidator/Services
```

---

## Step 2 — Project File

**File:** `IndexCsvConsolidator/IndexCsvConsolidator.csproj`

Replace the entire content of the generated `.csproj` file with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>IndexCsvConsolidator</AssemblyName>
    <RootNamespace>IndexCsvConsolidator</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="CsvHelper" Version="33.0.1" />
    <PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Binder" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <None Update="appsettings.json">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </None>
  </ItemGroup>

</Project>
```

---

## Step 3 — appsettings.json

**File:** `IndexCsvConsolidator/appsettings.json`

```json
{
  "Settings": {
    "InputFolder": "D:\\Data\\Input",
    "OutputFolder": "D:\\Data\\Output",
    "ArchiveFolder": "D:\\Data\\Archive",
    "OverwriteExistingValue": false,
    "AllowCreateMasterIfNotExists": false
  }
}
```

---

## Step 4 — Models

### Step 4.1 — AppSettings.cs

**File:** `IndexCsvConsolidator/Models/AppSettings.cs`

```csharp
namespace IndexCsvConsolidator.Models;

public class AppSettings
{
    public string InputFolder { get; set; } = string.Empty;
    public string OutputFolder { get; set; } = string.Empty;
    public string ArchiveFolder { get; set; } = string.Empty;
    public bool OverwriteExistingValue { get; set; } = false;
    public bool AllowCreateMasterIfNotExists { get; set; } = false;
}
```

---

### Step 4.2 — InputRecord.cs

**File:** `IndexCsvConsolidator/Models/InputRecord.cs`

This class represents one parsed row from an input CSV file. All fields are strings. Empty string means the column was absent or empty in the input.

```csharp
namespace IndexCsvConsolidator.Models;

public class InputRecord
{
    public string IndexName     { get; set; } = string.Empty;
    public string Date          { get; set; } = string.Empty;
    public string Open          { get; set; } = string.Empty;
    public string High          { get; set; } = string.Empty;
    public string Low           { get; set; } = string.Empty;
    public string Close         { get; set; } = string.Empty;
    public string SharesTraded  { get; set; } = string.Empty;
    public string TurnoverInrCr { get; set; } = string.Empty;
    public string PE            { get; set; } = string.Empty;
    public string PB            { get; set; } = string.Empty;
    public string DividendYield { get; set; } = string.Empty;
}
```

---

### Step 4.3 — MasterRecord.cs

**File:** `IndexCsvConsolidator/Models/MasterRecord.cs`

This class represents one row in a master CSV file. All fields are strings. Empty string means no data for that field on that date.

```csharp
namespace IndexCsvConsolidator.Models;

public class MasterRecord
{
    public string IndexName     { get; set; } = string.Empty;
    public string Date          { get; set; } = string.Empty;
    public string Open          { get; set; } = string.Empty;
    public string High          { get; set; } = string.Empty;
    public string Low           { get; set; } = string.Empty;
    public string Close         { get; set; } = string.Empty;
    public string SharesTraded  { get; set; } = string.Empty;
    public string TurnoverInrCr { get; set; } = string.Empty;
    public string PE            { get; set; } = string.Empty;
    public string PB            { get; set; } = string.Empty;
    public string DividendYield { get; set; } = string.Empty;
}
```

---

## Step 5 — Services

### Step 5.1 — LogService.cs

**File:** `IndexCsvConsolidator/Services/LogService.cs`

Rules:
- Constructor creates the log folder if it does not exist.
- Log file name: `run_YYYYMMDD_HHmmss.log`
- Every message is written to both console and log file simultaneously.
- `Warning` writes in Yellow, `Error` in Red, `Conflict` in Magenta, `Summary` in Cyan.
- Console colour is reset after each coloured write.
- `Dispose` writes a closing line then closes the file.

```csharp
namespace IndexCsvConsolidator.Services;

public class LogService : IDisposable
{
    private readonly StreamWriter _writer;
    private bool _disposed;

    public LogService(string logFolder)
    {
        Directory.CreateDirectory(logFolder);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string logPath = Path.Combine(logFolder, $"run_{timestamp}.log");
        _writer = new StreamWriter(logPath, append: false) { AutoFlush = true };
        WriteLine("INFO ", $"=== IndexCsvConsolidator started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
    }

    public void Info(string message)     => WriteLine("INFO ", message);

    public void Warning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        WriteLine("WARN ", message);
        Console.ResetColor();
    }

    public void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        WriteLine("ERROR", message);
        Console.ResetColor();
    }

    public void Conflict(string message)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        WriteLine("CONF ", message);
        Console.ResetColor();
    }

    public void Summary(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        WriteLine("SUMM ", message);
        Console.ResetColor();
    }

    private void WriteLine(string level, string message)
    {
        string line = $"[{level}] {DateTime.Now:HH:mm:ss}  {message}";
        Console.WriteLine(line);
        _writer.WriteLine(line);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        WriteLine("INFO ", $"=== IndexCsvConsolidator ended at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        _writer.Dispose();
    }
}
```

---

### Step 5.2 — DateHelper.cs

**File:** `IndexCsvConsolidator/Services/DateHelper.cs`

Rules:
- `TryNormalizeDate` accepts input date strings and converts them to the standard output format `dd-MMM-yy` (e.g. `24-Dec-25`).
- Accepted input formats: `dd MMM yyyy`, `d MMM yyyy`, `dd-MMM-yy`, `d-MMM-yy`. No other formats are accepted.
- Returns `false` if input is null, whitespace, or does not match any accepted format.
- `TryParseOutputDate` parses a date string already in output format back to `DateTime` for sorting.
- All parsing uses `CultureInfo.InvariantCulture` to prevent locale-dependent behaviour.

```csharp
using System.Globalization;

namespace IndexCsvConsolidator.Services;

public static class DateHelper
{
    private static readonly string[] InputFormats =
    {
        "dd MMM yyyy",
        "d MMM yyyy",
        "dd-MMM-yy",
        "d-MMM-yy"
    };

    private static readonly string[] OutputFormats =
    {
        "dd-MMM-yy",
        "d-MMM-yy"
    };

    private const string OutputFormat = "dd-MMM-yy";

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
            OutputFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);
    }
}
```

---

### Step 5.3 — IndexNameNormalizer.cs

**File:** `IndexCsvConsolidator/Services/IndexNameNormalizer.cs`

Rules:
1. Trim leading and trailing whitespace.
2. Replace one or more consecutive whitespace characters with a single hyphen `-`.
3. Remove any character that is invalid in a Windows file name (`Path.GetInvalidFileNameChars()`).
4. Throw `ArgumentException` if the input is null or whitespace.

```csharp
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

        // Replace one or more whitespace characters with a single hyphen
        result = Regex.Replace(result, @"\s+", "-");

        // Remove invalid filename characters (excluding hyphen which is valid)
        foreach (char c in InvalidFileNameChars)
            result = result.Replace(c.ToString(), string.Empty);

        return result;
    }
}
```

---

### Step 5.4 — InputFileParser.cs

**File:** `IndexCsvConsolidator/Services/InputFileParser.cs`

Rules:
- Returns `null` if validation fails (invalid file stays in input folder).
- Returns an empty `List<InputRecord>` if file is valid but has no data rows.
- Validation: must have `Date` column AND (`IndexName` OR `Index Name`) column.
- Column name matching is case-insensitive.
- Input column `Index Name` → `InputRecord.IndexName`.
- Input column `P/E` → `InputRecord.PE`.
- Input column `P/B` → `InputRecord.PB`.
- Input column `Div Yield %` → `InputRecord.DividendYield`.
- All other column names match directly (case-insensitive).
- Completely blank rows (both IndexName and Date empty) are skipped silently.
- All field values are trimmed.
- Catches all exceptions, logs them, returns `null`.
- Uses manual header-to-index mapping (not CsvHelper auto-mapping) for robustness.

```csharp
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
                HasHeaderRecord  = true,
                TrimOptions      = TrimOptions.Trim,
                MissingFieldFound = null,
                HeaderValidated  = null,
            };

            using var reader = new StreamReader(filePath);
            using var csv    = new CsvReader(reader, config);

            // Read header row
            csv.Read();
            csv.ReadHeader();

            var rawHeaders = csv.HeaderRecord
                ?? throw new InvalidDataException("File has no header row.");

            var headers = rawHeaders
                .Select(h => h?.Trim() ?? string.Empty)
                .ToList();

            // Validate required columns
            bool hasDate  = headers.Any(h => h.Equals("Date", StringComparison.OrdinalIgnoreCase));
            bool hasIndex = headers.Any(h =>
                h.Equals("IndexName",   StringComparison.OrdinalIgnoreCase) ||
                h.Equals("Index Name",  StringComparison.OrdinalIgnoreCase));

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

            // Build header-to-column-index map (case-insensitive, first occurrence wins)
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

                // Local helper: get trimmed value by column name(s), returns empty if not found
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
                    IndexName     = GetVal("IndexName", "Index Name"),
                    Date          = GetVal("Date"),
                    Open          = GetVal("Open"),
                    High          = GetVal("High"),
                    Low           = GetVal("Low"),
                    Close         = GetVal("Close"),
                    SharesTraded  = GetVal("SharesTraded"),
                    TurnoverInrCr = GetVal("TurnoverInrCr"),
                    PE            = GetVal("P/E"),
                    PB            = GetVal("P/B"),
                    DividendYield = GetVal("Div Yield %"),
                };

                // Skip completely blank rows
                if (string.IsNullOrWhiteSpace(record.IndexName) &&
                    string.IsNullOrWhiteSpace(record.Date))
                    continue;

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
```

---

### Step 5.5 — MasterCsvRepository.cs

**File:** `IndexCsvConsolidator/Services/MasterCsvRepository.cs`

Rules:
- `Load` returns an empty list if the file does not exist.
- `Load` uses `MasterRecordMap` to map column names to properties.
- `Save` overwrites the file completely (not append).
- `Save` uses `MasterRecordMap` to control exact column order and names.
- Both `Load` and `Save` use `CultureInfo.InvariantCulture`.
- `MasterRecordMap` is defined in the same file.
- Column order in output is always: `IndexName, Date, Open, High, Low, Close, SharesTraded, TurnoverInrCr, PE, PB, DividendYield`.

```csharp
using CsvHelper;
using CsvHelper.Configuration;
using IndexCsvConsolidator.Models;
using System.Globalization;

namespace IndexCsvConsolidator.Services;

public class MasterCsvRepository
{
    public List<MasterRecord> Load(string filePath)
    {
        if (!File.Exists(filePath))
            return new List<MasterRecord>();

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord   = true,
            TrimOptions       = TrimOptions.Trim,
            MissingFieldFound = null,
            HeaderValidated   = null,
        };

        using var reader = new StreamReader(filePath);
        using var csv    = new CsvReader(reader, config);
        csv.Context.RegisterClassMap<MasterRecordMap>();

        return csv.GetRecords<MasterRecord>().ToList();
    }

    public void Save(string filePath, List<MasterRecord> records)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
        };

        using var writer = new StreamWriter(filePath, append: false);
        using var csv    = new CsvWriter(writer, config);
        csv.Context.RegisterClassMap<MasterRecordMap>();
        csv.WriteRecords(records);
    }
}

public sealed class MasterRecordMap : ClassMap<MasterRecord>
{
    public MasterRecordMap()
    {
        Map(m => m.IndexName    ).Index(0) .Name("IndexName");
        Map(m => m.Date         ).Index(1) .Name("Date");
        Map(m => m.Open         ).Index(2) .Name("Open");
        Map(m => m.High         ).Index(3) .Name("High");
        Map(m => m.Low          ).Index(4) .Name("Low");
        Map(m => m.Close        ).Index(5) .Name("Close");
        Map(m => m.SharesTraded ).Index(6) .Name("SharesTraded");
        Map(m => m.TurnoverInrCr).Index(7) .Name("TurnoverInrCr");
        Map(m => m.PE           ).Index(8) .Name("PE");
        Map(m => m.PB           ).Index(9) .Name("PB");
        Map(m => m.DividendYield).Index(10).Name("DividendYield");
    }
}
```

---

### Step 5.6 — FileProcessorService.cs

**File:** `IndexCsvConsolidator/Services/FileProcessorService.cs`

Rules:
- `ProcessAll` scans `InputFolder` for `*.csv` files only.
- Each file is processed independently. A failure in one file does not stop processing of remaining files.
- `ProcessFile` returns `true` on success, `false` on any failure.
- A file is only archived if it was processed fully without error.
- A file with no data rows is archived with a warning (not an error).
- The merge method `Merge` uses reflection to get/set `MasterRecord` properties by name. Property names passed in must exactly match `MasterRecord` property names.
- **Intra-file duplicate date rule:** the first row for a date is kept; all subsequent rows for the same date in the same file are skipped and logged as CONF.
- **Cross-file / master conflict rule:** if incoming non-empty value differs from existing non-empty value:
  - If `OverwriteExistingValue = false`: keep existing, log CONF.
  - If `OverwriteExistingValue = true`: overwrite with incoming, log CONF.
- If incoming value is empty/whitespace: do nothing, no log.
- If existing value is empty/whitespace and incoming is non-empty: write incoming, no log.
- If existing and incoming are equal (case-insensitive): do nothing, no log.
- After merging all records, sort `masterRecords` list by ascending date using `DateHelper.TryParseOutputDate`.
- `ArchiveFile` deletes the destination file first if it already exists (overwrite on archive collision).

```csharp
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
        _settings   = settings;
        _log        = log;
        _parser     = new InputFileParser(log);
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

        int total   = files.Length;
        int success = 0;
        int failed  = 0;

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
        string fileName    = Path.GetFileName(filePath);
        int    conflictCount = 0;

        try
        {
            // Step 1: Parse input file
            List<InputRecord>? inputRecords = _parser.Parse(filePath);
            if (inputRecords == null)
                return false; // error already logged by parser

            // Step 2: Handle empty file (valid structure but no data rows)
            if (inputRecords.Count == 0)
            {
                _log.Warning($"File '{fileName}': No data rows found. Archiving without merge.");
                ArchiveFile(filePath);
                return true;
            }

            // Step 3: Extract raw index name from first record that has one
            string rawIndexName = inputRecords
                .Select(r => r.IndexName)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(rawIndexName))
            {
                _log.Error($"File '{fileName}': No valid IndexName found in any data row. File will not be archived.");
                return false;
            }

            // Step 4: Normalize index name
            string normalizedIndexName = IndexNameNormalizer.Normalize(rawIndexName);

            // Step 5: Determine master file path
            string masterPath = Path.Combine(
                _settings.OutputFolder,
                $"Main_{normalizedIndexName}.csv");

            // Step 5a: Check master file exists when AllowCreateMasterIfNotExists = false
            if (!File.Exists(masterPath) && !_settings.AllowCreateMasterIfNotExists)
            {
                _log.Error(
                    $"File '{fileName}': Master file for index '{normalizedIndexName}' " +
                    $"not found in output folder. Input file will not be archived.");
                return false;
            }

            // Step 6: Load existing master records
            List<MasterRecord> masterRecords = _repository.Load(masterPath);

            // Build dictionary keyed by "IndexName|Date" composite for O(1) lookup.
            // Using both fields ensures we only patch a record when both the index
            // name AND the date match — more precise than date alone.
            var masterDict = new Dictionary<string, MasterRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (MasterRecord r in masterRecords)
                masterDict[$"{r.IndexName}|{r.Date}"] = r;

            // Track dates encountered in THIS input file (for intra-file duplicate detection)
            var seenDatesInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Step 7: Merge each input row into master
            foreach (InputRecord input in inputRecords)
            {
                // Normalise input date
                if (!DateHelper.TryNormalizeDate(input.Date, out string normalizedDate))
                {
                    _log.Error($"File '{fileName}': Cannot parse date '{input.Date}'. Row skipped.");
                    continue;
                }

                // Detect intra-file duplicate date
                if (!seenDatesInFile.Add(normalizedDate))
                {
                    _log.Conflict(
                        $"File '{fileName}' | Date '{normalizedDate}' | " +
                        $"Duplicate date within input file — first row kept, this row skipped.");
                    conflictCount++;
                    continue;
                }

                // Get or create master record for this IndexName+Date combination
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

                // Merge all fields
                Merge(masterRecord, nameof(MasterRecord.Open),          input.Open,          fileName, normalizedDate, ref conflictCount);
                Merge(masterRecord, nameof(MasterRecord.High),          input.High,          fileName, normalizedDate, ref conflictCount);
                Merge(masterRecord, nameof(MasterRecord.Low),           input.Low,           fileName, normalizedDate, ref conflictCount);
                Merge(masterRecord, nameof(MasterRecord.Close),         input.Close,         fileName, normalizedDate, ref conflictCount);
                Merge(masterRecord, nameof(MasterRecord.SharesTraded),  input.SharesTraded,  fileName, normalizedDate, ref conflictCount);
                Merge(masterRecord, nameof(MasterRecord.TurnoverInrCr), input.TurnoverInrCr, fileName, normalizedDate, ref conflictCount);
                Merge(masterRecord, nameof(MasterRecord.PE),            input.PE,            fileName, normalizedDate, ref conflictCount);
                Merge(masterRecord, nameof(MasterRecord.PB),            input.PB,            fileName, normalizedDate, ref conflictCount);
                Merge(masterRecord, nameof(MasterRecord.DividendYield), input.DividendYield, fileName, normalizedDate, ref conflictCount);
            }

            // Step 8: Sort master records by ascending date
            masterRecords.Sort((a, b) =>
            {
                DateHelper.TryParseOutputDate(a.Date, out DateTime da);
                DateHelper.TryParseOutputDate(b.Date, out DateTime db);
                return da.CompareTo(db);
            });

            // Step 9: Save master CSV (overwrite entirely)
            Directory.CreateDirectory(_settings.OutputFolder);
            _repository.Save(masterPath, masterRecords);

            // Step 10: Archive input file
            ArchiveFile(filePath);

            _log.Info(
                $"File '{fileName}' → '{Path.GetFileName(masterPath)}' | " +
                $"Master rows: {masterRecords.Count} | Conflicts: {conflictCount}");

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
        string       propertyName,
        string       incomingValue,
        string       fileName,
        string       date,
        ref int      conflictCount)
    {
        // Nothing to merge if incoming is empty
        if (string.IsNullOrWhiteSpace(incomingValue))
            return;

        var prop = typeof(MasterRecord).GetProperty(propertyName)
            ?? throw new InvalidOperationException(
                $"Property '{propertyName}' does not exist on MasterRecord.");

        string existingValue = (string)(prop.GetValue(masterRecord) ?? string.Empty);

        if (string.IsNullOrWhiteSpace(existingValue))
        {
            // No existing value — write incoming
            prop.SetValue(masterRecord, incomingValue);
            return;
        }

        if (existingValue.Equals(incomingValue, StringComparison.OrdinalIgnoreCase))
            return; // Same value — no action required

        // Conflict: existing and incoming are both non-empty and different
        conflictCount++;

        if (_settings.OverwriteExistingValue)
        {
            _log.Conflict(
                $"OVERWRITE | File: '{fileName}' | Date: '{date}' | Field: '{propertyName}' | " +
                $"Was: '{existingValue}' | Now: '{incomingValue}'");
            prop.SetValue(masterRecord, incomingValue);
        }
        else
        {
            _log.Conflict(
                $"KEPT      | File: '{fileName}' | Date: '{date}' | Field: '{propertyName}' | " +
                $"Kept: '{existingValue}' | Ignored: '{incomingValue}'");
        }
    }

    private void ArchiveFile(string filePath)
    {
        Directory.CreateDirectory(_settings.ArchiveFolder);
        string destination = Path.Combine(_settings.ArchiveFolder, Path.GetFileName(filePath));

        if (File.Exists(destination))
            File.Delete(destination);

        File.Move(filePath, destination);
    }
}
```

---

## Step 6 — Program.cs

**File:** `IndexCsvConsolidator/Program.cs`

Delete the entire generated content of `Program.cs` and replace with:

```csharp
using IndexCsvConsolidator.Models;
using IndexCsvConsolidator.Services;
using Microsoft.Extensions.Configuration;

// Load configuration from appsettings.json next to the executable
IConfiguration configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

AppSettings settings = configuration.GetSection("Settings").Get<AppSettings>()
    ?? throw new InvalidOperationException(
        "appsettings.json must contain a 'Settings' section. See README for format.");

// Validate input folder (must exist — cannot create it as contents are user-managed)
if (!Directory.Exists(settings.InputFolder))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[ERROR] Input folder not found: {settings.InputFolder}");
    Console.WriteLine("        Update InputFolder in appsettings.json and retry.");
    Console.ResetColor();
    Environment.Exit(1);
}

// Output and archive folders are created automatically if absent
Directory.CreateDirectory(settings.OutputFolder);
Directory.CreateDirectory(settings.ArchiveFolder);

// Logs are written to a Logs subfolder next to the executable
string logFolder = Path.Combine(AppContext.BaseDirectory, "Logs");

using LogService log = new(logFolder);

log.Info($"Input                      : {settings.InputFolder}");
log.Info($"Output                     : {settings.OutputFolder}");
log.Info($"Archive                    : {settings.ArchiveFolder}");
log.Info($"OverwriteExistingValue     : {settings.OverwriteExistingValue}");
log.Info($"AllowCreateMasterIfNotExists: {settings.AllowCreateMasterIfNotExists}");
log.Info(string.Empty);

FileProcessorService processor = new(settings, log);
processor.ProcessAll();
```

---

## Step 7 — Restore and Build

Run from the solution root (where `IndexCsvConsolidator.sln` is):

```bash
dotnet restore
dotnet build
```

Build must complete with zero errors before proceeding.

---

## Step 8 — Verify Compilation

Run a quick smoke test to confirm the exe starts and reads config:

```bash
cd IndexCsvConsolidator
dotnet run
```

Expected output when InputFolder does not exist:

```
[ERROR] Input folder not found: D:\Data\Input
        Update InputFolder in appsettings.json and retry.
```

Expected output when InputFolder exists but is empty:

```
[INFO ] HH:mm:ss  === IndexCsvConsolidator started at ... ===
[INFO ] HH:mm:ss  Input              : D:\Data\Input
[INFO ] HH:mm:ss  Output             : D:\Data\Output
[INFO ] HH:mm:ss  Archive            : D:\Data\Archive
[INFO ] HH:mm:ss  OverwriteExisting  : False
[INFO ] HH:mm:ss  
[INFO ] HH:mm:ss  No CSV files found in input folder.
[SUMM ] HH:mm:ss  Run complete — Total: 0 | Success: 0 | Failed: 0
[INFO ] HH:mm:ss  === IndexCsvConsolidator ended at ... ===
```

---

## Step 9 — Publish Single EXE

Run from the solution root:

```bash
dotnet publish IndexCsvConsolidator/IndexCsvConsolidator.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Copy `appsettings.json` to the `publish` folder:

```bash
copy IndexCsvConsolidator\appsettings.json publish\appsettings.json
```

Edit `publish\appsettings.json` with production folder paths before distributing.

---

## Behaviour Reference (for verification)

### Successful price + valuation merge

**Existing master** (`Main_NIFTY-PRIVATE-BANK.csv`):
```
IndexName,Date,Open,High,Low,Close,SharesTraded,TurnoverInrCr,PE,PB,DividendYield
NIFTY-PRIVATE-BANK,23-Dec-25,28543.85,28565.80,28470.30,28501.55,,,20.08,2.21,0.53
```

**Input: price file**:
```
Index Name,Date,Open,High,Low,Close
NIFTY PRIVATE BANK,24 Dec 2025,28531.00,28614.15,28429.05,28460.80
```

**Input: valuation file**:
```
IndexName,Date,P/E,P/B,Div Yield %
NIFTY PRIVATE BANK,24 Dec 2025,20.05,2.21,0.53
```

**Final master after both files processed**:
```
IndexName,Date,Open,High,Low,Close,SharesTraded,TurnoverInrCr,PE,PB,DividendYield
NIFTY-PRIVATE-BANK,23-Dec-25,28543.85,28565.80,28470.30,28501.55,,,20.08,2.21,0.53
NIFTY-PRIVATE-BANK,24-Dec-25,28531.00,28614.15,28429.05,28460.80,,,20.05,2.21,0.53
```

### Conflict log example (`OverwriteExistingValue = false`)

```
[CONF ] 10:15:22  KEPT      | File: 'prices_dec.csv' | Date: '24-Dec-25' | Field: 'Close' | Kept: '28460.80' | Ignored: '28500.00'
```

### Invalid file example

```
[ERROR] 10:15:22  File 'bad_file.csv': Missing required 'Date' column. File will not be archived.
```

---

## Acceptance Checklist

After implementation, verify each point:

- [ ] `dotnet build` produces zero errors and zero warnings
- [ ] `AllowCreateMasterIfNotExists = false` — input file is skipped with error log when master file is missing; file stays in input folder
- [ ] `AllowCreateMasterIfNotExists = true` — master file is created automatically when missing
- [ ] Processing a price CSV creates `Main_{Index}.csv` with correct columns and date format
- [ ] Processing a valuation CSV merges PE, PB, DividendYield into existing master row for same date
- [ ] A new date in input is inserted as a new row in master
- [ ] Master remains sorted ascending by date after every run
- [ ] Successfully processed files move to ArchiveFolder
- [ ] Files that fail validation remain in InputFolder
- [ ] Conflicts are logged with CONF level and show existing vs incoming value
- [ ] `OverwriteExistingValue = true` causes conflicting field to be overwritten
- [ ] `OverwriteExistingValue = false` causes conflicting field to be kept as-is
- [ ] Run summary line shows correct Total / Success / Failed counts
- [ ] A `Logs/run_*.log` file is created for every run
- [ ] Empty input file (header only) is archived with a WARN log and no merge performed