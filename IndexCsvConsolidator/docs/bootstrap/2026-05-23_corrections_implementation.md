# Index CSV Consolidator — Correction Plan

> **For AI Coding Agents:** Apply only the changes described below.
> Do not modify anything not mentioned here.

---

## Corrections Overview

| # | File | Issue | Fix |
|---|---|---|---|
| 1 | `Services/DateHelper.cs` | Output date format is `dd-MMM-yy` (2-digit year) — does not match real master data which uses 4-digit year. Date lookup key mismatches and sort fails. | Change format constant and all format arrays to `dd-MMM-yyyy` |
| 2 | `Services/MasterCsvRepository.cs` | CsvHelper default write config does not quote all fields — changes the format of the master file on every save | Add `ShouldQuote = _ => true` to write config. Add UTF-8 BOM to both read and write so Excel opens correctly. |

---

## Correction 1 — DateHelper.cs

**File:** `Services/DateHelper.cs`

Replace the entire file content with the following:

```csharp
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
```

**What changed:**
- `OutputFormat` constant: `"dd-MMM-yy"` → `"dd-MMM-yyyy"`
- `InputFormats` array: `yy` → `yyyy` in all entries
- `OutputParseFormats` array: `yy` → `yyyy` in all entries

---

## Correction 2 — MasterCsvRepository.cs

**File:** `Services/MasterCsvRepository.cs`

Replace the entire file content with the following:

```csharp
using CsvHelper;
using CsvHelper.Configuration;
using IndexCsvConsolidator.Models;
using System.Globalization;
using System.Text;

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

        using var reader = new StreamReader(filePath,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        using var csv = new CsvReader(reader, config);
        csv.Context.RegisterClassMap<MasterRecordMap>();
        return csv.GetRecords<MasterRecord>().ToList();
    }

    public void Save(string filePath, List<MasterRecord> records)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,

            // Quote every field — preserves the original Excel CSV format
            ShouldQuote = _ => true,
        };

        // UTF-8 with BOM — Excel opens without encoding prompt
        using var writer = new StreamWriter(filePath, append: false,
            encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        using var csv = new CsvWriter(writer, config);
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

**What changed:**
- `Load`: `StreamReader` uses `UTF8Encoding(true)` — BOM-aware reading
- `Save`: `ShouldQuote = _ => true` — every field always quoted
- `Save`: `StreamWriter` uses `UTF8Encoding(true)` — writes UTF-8 with BOM
- `MasterRecordMap` is unchanged

---

## Verification After Applying Corrections

- [ ] `dotnet build` produces zero errors
- [ ] Date in master CSV uses 4-digit year: `30-Dec-1999` not `30-Dec-99`
- [ ] PE, PB, DividendYield merged into correct row — matched by IndexName + Date
- [ ] Records in master CSV are sorted ascending by date
- [ ] Every field in master CSV is quoted: `"NIFTY-50","30-Dec-1999","1489.2",...`
- [ ] Empty fields are quoted empty: `"","",""`— not unquoted
- [ ] File opens in Excel without any encoding prompt
- [ ] Running the program twice on the same master produces identical output