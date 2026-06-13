# Index CSV Consolidation System - Requirements

## 1. Goal

Consolidate multiple CSV input files into a single master CSV file per index.

The system must:

- Read CSV files from an input folder
- Identify index using `IndexName` or `Index Name` column
- Normalize index name
- Create/update master CSV for that index
- Merge records by `Date`
- Preserve existing non-empty values
- Move successfully processed files to archive folder
- Leave failed files in input folder

Consumers should only need to read one CSV file per index.

---

# 2. Folder Configuration

| Configuration | Purpose |
|---|---|
| Input Folder | Incoming CSV files |
| Output Folder | Master consolidated CSV files |
| Archive Folder | Successfully processed files |

Example:

```text
InputFolder   = D:\Data\Input
OutputFolder  = D:\Data\Output
ArchiveFolder = D:\Data\Archive
```

---

# 3. Input File Rules

- Input files are CSV files with header row
- One input file contains data for only one index
- File name is irrelevant
- Index must be identified from record data

---

## 3.1 Price Input Example

```csv
Index Name,Date,Open,High,Low,Close
NIFTY PRIVATE BANK,24 Dec 2025,28531.00,28614.15,28429.05,28460.80
```

Supported columns:

- Index Name
- Date
- Open
- High
- Low
- Close
- SharesTraded
- TurnoverInrCr

---

## 3.2 Valuation Input Example

```csv
IndexName,Date,P/E,P/B,Div Yield %
NIFTY PRIVATE BANK,24 Dec 2025,20.05,2.21,0.53
```

Supported columns:

- IndexName
- Date
- P/E
- P/B
- Div Yield %

---

# 4. Index Name Normalization

Normalization rules:

1. Trim spaces
2. Replace whitespace with `-`
3. Remove invalid filename characters only

Example:

```text
NIFTY PRIVATE BANK
```

becomes:

```text
NIFTY-PRIVATE-BANK
```

The normalized value must be used:

- In output file name
- In output `IndexName` column

---

# 5. Output File Rules

One master file per index.

File naming format:

```text
Main_{NormalizedIndexName}.csv
```

Example:

```text
Main_NIFTY-PRIVATE-BANK.csv
```

Create file automatically if it does not exist.

---

## 5.1 Output Column Structure

```csv
IndexName,Date,Open,High,Low,Close,SharesTraded,TurnoverInrCr,PE,PB,DividendYield
```

---

## 5.2 Column Mapping

| Input Column | Output Column |
|---|---|
| Index Name | IndexName |
| P/E | PE |
| P/B | PB |
| Div Yield % | DividendYield |

---

## 5.3 Date Format

Output format:

```text
24-Dec-25
```

Master CSV must remain sorted by ascending date.

---

# 6. Merge Rules

Merge key:

```text
Date
```

Rules:

- Update existing row if date exists
- Insert new row if date does not exist
- Update only fields available in input
- Never overwrite existing value with null, empty, or whitespace
- Leave unavailable fields unchanged

Example:

Existing:

```csv
IndexName,Date,Close,PE
NIFTY-PRIVATE-BANK,24-Dec-25,28460.80,20.05
```

Incoming:

```csv
Index Name,Date,Close
NIFTY PRIVATE BANK,24 Dec 2025,28500.00
```

Result:

```csv
IndexName,Date,Close,PE
NIFTY-PRIVATE-BANK,24-Dec-25,28500.00,20.05
```

---

# 7. Processing Flow

```text
1. Read files from input folder
2. Process files one by one
3. Read records
4. Detect index name
5. Normalize index name
6. Load/create master CSV
7. Merge records
8. Sort by ascending date
9. Save master CSV
10. Move processed file to archive folder
```

If processing fails:

- File must remain in input folder
- Processing should continue for remaining files

---

# 8. Validation Rules

Input files must:

- Have header row
- Have valid CSV structure
- Contain Date column
- Contain IndexName or Index Name column

Invalid files:

- Must not be archived
- Must remain in input folder

---

# 9. Input Type Detection

## Price Input

Detected using columns like:

- Open
- High
- Low
- Close

## Valuation Input

Detected using columns like:

- P/E
- P/B
- Div Yield %

---

# 10. Sample Consolidation

## Existing Master

```csv
IndexName,Date,Open,High,Low,Close,SharesTraded,TurnoverInrCr,PE,PB,DividendYield
NIFTY-PRIVATE-BANK,23-Dec-25,28543.85,28565.80,28470.30,28501.55,,,20.08,2.21,0.53
```

## Incoming Price

```csv
Index Name,Date,Open,High,Low,Close
NIFTY PRIVATE BANK,24 Dec 2025,28531.00,28614.15,28429.05,28460.80
```

## Incoming Valuation

```csv
IndexName,Date,P/E,P/B,Div Yield %
NIFTY PRIVATE BANK,24 Dec 2025,20.05,2.21,0.53
```

## Final Output

```csv
IndexName,Date,Open,High,Low,Close,SharesTraded,TurnoverInrCr,PE,PB,DividendYield
NIFTY-PRIVATE-BANK,23-Dec-25,28543.85,28565.80,28470.30,28501.55,,,20.08,2.21,0.53
NIFTY-PRIVATE-BANK,24-Dec-25,28531.00,28614.15,28429.05,28460.80,,,20.05,2.21,0.53
```

---

# 11. Acceptance Criteria

Implementation is successful when:

- One master CSV exists per index
- Records are merged correctly by date
- Existing values are preserved
- Output structure remains consistent
- Master CSV remains sorted by ascending date
- Successfully processed files are archived
- Failed files remain in input folder