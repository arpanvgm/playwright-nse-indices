# niftyindices.com — Playwright Automation Reference

Authoritative reference for automating `https://niftyindices.com/reports/historical-data`. All facts here are confirmed from live DOM inspection and working automation runs. Read this before writing any Playwright code that touches this site.

## Site Architecture

- The page is an ASP.NET WebForms SPA.
- It never fully reloads. All report tabs share one HTML document.
- Inactive tab elements remain in the DOM but are hidden (`offsetParent === null`).
- **Tab switching is strictly client-side**: The site uses a nested dropdown menu for section navigation. Clicking a section header toggles UI visibility internally. It **does not** trigger an AJAX request.
- Every dropdown change and Submit click fires an AJAX POST. The server returns partial HTML which the page's own JS renders.
- Third-party analytics scripts run indefinitely — NetworkIdle never resolves on this page.

## Report Sections

The site presents report sections via a dropdown menu. Clicking a section (`<li>`) activates that report visually on the client side.

**Section selectors (confirmed from live DOM inspection)**

| Report Section | LI Selector | Notes |
|---|---|---|
| Historical Index Data | `li.form1` | Plain JS click — no child anchor |
| P/E, P/B & Div.Yield | `li.form4` | Plain JS click — no child anchor |

## Report Tabs and Their Selectors

Each section has its own isolated set of elements and date inputs.
Inactive section elements are present in the DOM but hidden — always filter by visibility before interacting.

**Full selector reference per report**

| Report | Section | Index Type Dropdown | Sub-Index Dropdown | Index Name Dropdown |
|---|---|---|---|---|
| Historical Data | `li.form1` | `#ddlHistoricaltypee` | `#ddlHistoricaltypeeSubindex` | `#ddlHistoricaltypeeindex` |
| P/E, P/B & Div.Yield | `li.form4` | `#ddlHistoricaldivtypee` | `#ddlHistoricaldivtypeeSubindex` | `#ddlHistoricaldivtypeeindex` |

**Index Type dropdown values (confirmed)**
Dropdown 1 (`#ddlHistoricaltypee`) options: `--Select--`, `Equity`, `Fixed Income`, `Multi Asset`.
The automation always selects `Equity`.

## Date, Submit, and CSV selectors

| Report Tab | From Date | To Date | Submit | CSV Link |
|---|---|---|---|---|
| Historical Data | `#datepickerFrom` | `#datepickerTo` | `#submit_button` | `#exporthistorical` |
| P/E, P/B & Div.Yield | `#datepickerFromDivYield` | `#datepickerToDivYield` | `#submit_buttonDivdata` | `#exporthistoricaldiv` |

## AJAX URL fragments

Each Submit click POSTs to the server. Listen for the response URL to detect completion.
The Index Type dropdown change also triggers AJAX — listen for the same fragment.
*(Note: Section clicks do NOT trigger AJAX).*

| Report Tab | AjaxUrlFragment |
|---|---|
| Historical Data | `Backpage.aspx` |
| P/E, P/B & Div.Yield | `Backpage.aspx` |

## Startup Navigation (Automated)

`StartupNavigator.PrepareReportAsync` handles:
1. Click section `<li>` → wait 1 s for UI to settle (client-side only, no AJAX).
2. Select "Equity" in Index Type dropdown → wait for AJAX (`Backpage.aspx`, 200 OK) → 1 s settle.

If the dropdown AJAX times out (15 s), `PrepareReportAsync` returns `NavigationResult(false)` and `Program.cs` aborts the entire run immediately. There is no point continuing if startup fails.

## Dropdown Interaction Rules

**1. Always target dropdowns by their specific ID**
Never pass null to scan all visible selects. Multiple dropdowns are visible simultaneously and the wrong one will be matched.

**2. Fire jQuery change event after setting a dropdown value**
The site uses jQuery event handlers to trigger AJAX chains. Setting `.value` alone does nothing. Always fire the `change` event after setting the value.

**3. Cascade order is strict — wait for AJAX between steps**
- Selecting Index Type → triggers AJAX → populates Sub-Index dropdown
- Selecting Sub-Index → triggers AJAX → populates Index Name dropdown
- Selecting Index Name → no AJAX, UI state only

Never attempt to select a downstream dropdown before its upstream AJAX has completed.

## Date Input Rules

**Use the jQuery UI datepicker API — not native input methods**
The date inputs are jQuery UI datepicker widgets. The Submit button reads the datepicker's internal state, not the visible input value. Native `FillAsync` or setting `.value` directly updates only the visible text — Submit ignores it.

Always pass dates as `dd-MM-yyyy` (e.g. `01-01-2020`).

## Clicking Elements

**Use JavaScript click — not Playwright's ClickAsync**
`ClickAsync` requires the element to be in the viewport and visible. Several elements on this page (Submit, CSV link, section LI) may be scrolled out of view or inside conditionally shown sections. JS click bypasses all visibility requirements.

## Detecting AJAX Completion

**The only reliable method: listen for the response event**
Set up the listener before triggering the action. Race it against a timeout. Always check which task won — do not ignore the return value of `Task.WhenAny`. 

**TaskCreationOptions.RunContinuationsAsynchronously**
Always pass this when constructing `TaskCompletionSource` instances that are completed from Playwright's response event handler to avoid deadlocks on Playwright's internal thread.

## Checking Whether Data Exists

The CSV link (`#exporthistorical`, `#exporthistoricaldiv`, etc.) is **always present in the DOM**. It is never added or removed — only shown or hidden via CSS.
- After AJAX completes: check `page.IsVisibleAsync(csvSelector)`
  - Visible → data exists → proceed to download
  - Hidden → no data for this date range → skip silently (the site shows no error)

## Page Load & Network Handling

**Use DOMContentLoaded, not Load**
Third-party analytics scripts on this page never finish loading. `WaitUntilState.Load` hangs indefinitely. Use `DOMContentLoaded` and swallow the timeout exception.

**Block heavy assets before navigating**
Blocking images, fonts, and media via `RouteAsync` speeds up the initial load significantly. (Do not use `RouteAsync` for passive observation, as it alters AJAX timing).

## Anti-Bot Detection

The site detects automation via two browser signals. Without masking both, dropdowns load slowly (10–30 seconds per change). With both masked, response is instant.
1. Mask `navigator.webdriver`
2. Disable `AutomationControlled` flag — use real Edge, not bundled Chromium.
