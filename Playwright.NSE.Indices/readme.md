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
git clone [https://github.com/your-username/your-repo-name.git](https://github.com/your-username/your-repo-name.git)
cd your-repo-name
~~~

### 2. Restore NuGet packages

~~~powershell
dotnet restore
~~~

This installs `Microsoft.Playwright` v1.59.0 as defined in the `.csproj` file.

### 3. Install Playwright browser drivers

Playwright needs browser binaries (Chromium, Firefox, WebKit) downloaded locally.
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

### 1. Script Parameters (`Program.cs`)
Open `Program.cs` and edit the **4 lines** at the top before running:

~~~csharp
const int    START_YEAR   = 2015;
// Starts on 1st January of this year
string END_DATE           = DateTime.Now.ToString("dd-MM-yyyy");
// runtime — today's date
const int    CHUNK_MONTHS = 12;
// Months per download chunk (12 = yearly)
const int    DELAY_MS     = 4000;
// Pause between downloads   (milliseconds)
~~~

### What is DELAY_MS?
A polite pause between each download request so you are not hammering the server.
`4000` (4 seconds) works well for normal use. Increase to `6000`–`8000` if you experience mid-run failures on large date ranges.

### What is CHUNK_MONTHS?
The site rejects date ranges that are too large.
The tool splits your full date range into smaller chunks of this many months each.
`12` (one year per request) works reliably. Reduce to `6` or `3` if the site starts rejecting requests.

### 2. Index Configuration (`indices.json`)
The tool dynamically reads the indices you want to download from `indices.json`.
You can easily skip certain indices by setting `"enabled": false` without having to delete them from the file:

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

### Switching report types

The selectors at the top of `Program.cs` are pre-configured for **P/E, P/B & Div.Yield**.
To download a different report type, change the four selector constants:

| Report Type | FROM_SEL | TO_SEL | SUBMIT_SEL | CSV_SEL |
|---|---|---|---|---|
| P/E, P/B & Div.Yield *(default)* | `#datepickerFromDivYield` | `#datepickerToDivYield` | `#submit_buttonDivdata` | `#exporthistoricaldiv` |
| Historical Data | `#datepickerFrom` | `#datepickerTo` | `#submit_button` | `#exporthistorical` |
| VIX Data | `#datepickerFromvixdata` | `#datepickerTovixdata` | `#submit_buttonvixdata` | `#exporthistoricalvix` |
| Total Index | `#datepickerFromtotalindex` | `#datepickerTototalindex` | `#submit_totalindexhistorical` | `#exportTotalindex` |

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

**4. Press ENTER to close the browser**

---

## Output files

Downloaded CSVs are saved to subfolders based on the report type inside the configured output folder (default `D:\MarketData\NseDownloads`).
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

## Troubleshooting

**Page takes too long to load / timeout on startup**

The site loads slowly due to third-party analytics scripts.
The tool handles this automatically by blocking heavy assets (images, fonts, media) to speed up loading and bypassing the analytics timeout after 15 seconds.
Just wait briefly — it will proceed.

**A chunk fails mid-run**

A `FAILED` line means the site returned no data for that date range (e.g. the index did not exist yet).
This is expected for early dates. The loop continues automatically to the next chunk.

**Dropdowns load slowly**

If the Sub-Index dropdown is slow to populate, the tool already uses your real Edge browser with bot-detection disabled.
If it persists, try closing other Edge windows before running.

---

## How it works (technical summary)

- Uses **Microsoft Playwright** to drive your real installed Edge browser (`Channel = "msedge"`)
- Disables the `AutomationControlled` Chrome flag and masks `navigator.webdriver` so the site does not detect automation
- Optimizes initial load times by aggressively aborting network requests for unused assets (images, fonts, media)
- Sets dates programmatically via the **jQuery UI datepicker API** (`datepicker('setDate', ...)`) which updates the datepicker's internal state — not just the visible input text
- Clicks Submit and the CSV link via direct JavaScript (`el.click()`) to bypass Playwright's visibility requirements
- All downloaded files are captured via Playwright's download interception and saved with descriptive filenames
