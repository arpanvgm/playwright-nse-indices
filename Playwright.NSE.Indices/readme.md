# NiftyIndices Historical Data Downloader

Automates downloading historical CSV data from [niftyindices.com/reports/historical-data](https://niftyindices.com/reports/historical-data).
The site enforces a date range limit in its UI, making bulk downloads tedious.

This tool lets you **select your base report once manually**, then automatically loops through your full date range and configured indices — downloading one CSV per chunk — and saves them all to a local folder.

---

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0 or later | Check with `dotnet --version` |
| [Microsoft Edge](https://www.microsoft.com/edge) | Any recent version | Must be installed — the tool drives your real Edge browser |
| PowerShell | 5.1+ | Pre-installed on Windows |

---

## Setup

### 1. Clone the repository

~~~powershell
git clone https://github.com/your-username/your-repo-name.git
cd your-repo-name
~~~

### 2. Restore NuGet packages

~~~powershell
dotnet restore
~~~

### 3. Install Playwright browser drivers

Playwright needs browser binaries downloaded locally.
Run these **once** after cloning:

~~~powershell
dotnet build
playwright install
~~~

Expected output — you should see each browser downloading:
~~~text
Downloading Chrome Headless Shell ...
Downloading Firefox 148.0.2 ...
Downloading WebKit 26.4 ...
Downloading FFmpeg ...
~~~

> If `playwright` is not recognised as a command, try the PowerShell script directly:
> ~~~powershell
> pwsh bin/Debug/net10.0/playwright.ps1 install
> ~~~

> If you get a scripts execution policy error, run this first and then retry:
> ~~~powershell
> Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
> ~~~

---

## Configuration

### 1. appsettings.json

All runtime parameters are configured in `appsettings.json` at the project root.
Edit this file before running — no need to touch any `.cs` files:

~~~json
{
  "DownloadsPath": "D:\\MarketData\\NseDownloads",
  "StartYear": 1996,
  "EndDate": "",
  "ChunkMonths": 12,
  "DelayMs": 4000
}
~~~

| Setting | Description | Default |
|---|---|---|
| `DownloadsPath` | Root folder where downloaded CSVs are saved | `D:\MarketData\NseDownloads` |
| `StartYear` | Download history starts on 1st January of this year | `1996` |
| `EndDate` | Last date to download (`dd-MM-yyyy`). Leave blank to use today's date | *(blank = today)* |
| `ChunkMonths` | Number of months per download chunk. Reduce to `6` or `3` if the site rejects requests | `12` |
| `DelayMs` | Polite pause between download requests (milliseconds). Increase to `6000`–`8000` for large runs | `4000` |

### 2. Index Configuration (`indices.json`)

The tool dynamically reads the indices you want to download from `indices.json`.
Disable an index without deleting it by setting `"enabled": false`:

~~~json
[
  {
    "name": "NIFTY 50",
    "subIndex": "Broad Market Indices",
    "enabled": true
  },
  {
    "name": "NIFTY NEXT 50",
    "subIndex": "Broad Market Indices",
    "enabled": false
  }
]
~~~

---

## Running the tool

~~~powershell
dotnet run
~~~

### What happens step by step

**1. Browser opens automatically**

Microsoft Edge launches and navigates to the NiftyIndices historical data page.

**2. You select the first 2 dropdowns (manual — one time only)**

The console will show:
~~~text
╔════════════════════════════════════════════════╗
║  Select the first 2 dropdowns in the browser:  ║
║   1. Report Type  (e.g. Historical Data)       ║
║   2. Index Type   (e.g. Equity)                ║
║                                                ║
║  Do NOT touch the Sub-Index or date fields.    ║
║  Press ENTER when dropdowns 1–2 are set.       ║
╚════════════════════════════════════════════════╝
~~~

Select the first two dropdowns in the browser window, then press **ENTER** in the console.
> Do **not** touch the Sub-Index, Index Name, or date fields — the tool auto-detects and fills those automatically based on `indices.json`.

**3. Automated download loop runs**

The tool takes over completely. For each enabled index and date chunk it:
- Auto-selects the Sub-Index and Index Name from the UI
- Sets the From and To dates via the jQuery datepicker API
- Clicks the Submit button
- Waits for data to render
- Clicks the CSV download link
- Saves the file to the output folder

**4. Press any key to close the browser**

---

## Output files

Downloaded CSVs are saved to subfolders based on the report type inside the configured `DownloadsPath`.
File naming format: `IndexName_ReportType_YYYY.csv`

Example:
~~~text
NseDownloads/
  Price/
    NIFTY_50_Price_2016.csv
    NIFTY_50_Price_2017.csv
  PE/
    NIFTY_50_PE_2016.csv
    NIFTY_50_PE_2017.csv
~~~

---

## Log files

Each run produces a timestamped log file in the application folder (next to the `.exe`):

~~~text
run_20260607_143022.log
~~~

The log file records the same output as the console, plus:
- Timestamps on every line
- `[ERROR]` tag with exception details for failures
- `[WARN]` tag for unexpected but non-fatal conditions

This makes it easy to review long unattended runs and grep for failures.

---

## Troubleshooting

**Page takes too long to load / timeout on startup**

The site loads slowly due to third-party analytics scripts.
The tool handles this automatically by blocking heavy assets and bypassing the analytics timeout.
Just wait briefly — it will proceed.

**A chunk shows "Skipped (no data for this period)"**

The site returned no data for that date range (e.g. the index did not exist yet).
This is expected for early dates. The loop continues automatically.

**A chunk shows "FAILED"**

An exception occurred (network error, page crash, download timeout).
The loop continues to the next chunk. Check the `.log` file for the full exception detail.

**Dropdowns load slowly**

The tool uses your real Edge browser with bot-detection disabled.
If it persists, try closing other Edge windows before running.

---

## How it works (technical summary)

- Uses **Microsoft Playwright** to drive your real installed Edge browser (`Channel = "msedge"`)
- Disables the `AutomationControlled` Chrome flag and masks `navigator.webdriver` so the site does not detect automation
- Optimizes initial load by aborting network requests for unused assets (images, fonts, media)
- Sets dates via the **jQuery UI datepicker API** (`datepicker('setDate', ...)`) which updates the datepicker's internal state — not just the visible input text
- Clicks Submit and the CSV link via direct JavaScript (`el.click()`) to bypass Playwright's visibility requirements
- All downloaded files are captured via Playwright's download interception and saved with descriptive filenames
- Configuration is driven by `appsettings.json` — no code changes needed for routine parameter adjustments
