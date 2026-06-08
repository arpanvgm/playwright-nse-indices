# niftyindices.com — Playwright Automation Reference

Authoritative reference for automating `https://niftyindices.com/reports/historical-data`.
All facts here are confirmed from live DOM inspection and working automation runs.
Read this before writing any Playwright code that touches this site.

---

## Site Architecture

The page is an ASP.NET WebForms SPA. It never fully reloads.
All report tabs share one HTML document. Inactive tab elements remain in the DOM but are hidden (`offsetParent === null`).
Every dropdown change and Submit click fires an AJAX POST. The server returns partial HTML which the page's own JS renders.
Third-party analytics scripts run indefinitely — `NetworkIdle` never resolves on this page.

---

## Report Tabs and Their Selectors

Each tab has its own isolated set of `<select>` elements and date inputs.
The inactive tab's elements are present in the DOM but hidden — always filter by visibility before interacting.

### Dropdown selectors

| Report Tab | Index Type Dropdown | Sub-Index Dropdown | Index Name Dropdown |
|---|---|---|---|
| Historical Data | `#ddlHistoricaltypee` | `#ddlHistoricaltypeeSubindex` | `#ddlHistoricaltypeeindex` |
| P/E, P/B & Div.Yield | `#ddlHistoricaldivtypee` | `#ddlHistoricaldivtypeeSubindex` | `#ddlHistoricaldivtypeeindex` |
| VIX Data | TBC | TBC | TBC |
| Total Index | TBC | TBC | TBC |

### Date, Submit, and CSV selectors

| Report Tab | From Date | To Date | Submit | CSV Link |
|---|---|---|---|---|
| Historical Data | `#datepickerFrom` | `#datepickerTo` | `#submit_button` | `#exporthistorical` |
| P/E, P/B & Div.Yield | `#datepickerFromDivYield` | `#datepickerToDivYield` | `#submit_buttonDivdata` | `#exporthistoricaldiv` |
| VIX Data | `#datepickerFromvixdata` | `#datepickerTovixdata` | `#submit_buttonvixdata` | `#exporthistoricalvix` |
| Total Index | `#datepickerFromtotalindex` | `#datepickerTototalindex` | `#submit_totalindexhistorical` | `#exportTotalindex` |

### AJAX URL fragments

Each Submit click POSTs to the server. Listen for the response URL to detect completion.

| Report Tab | AjaxUrlFragment |
|---|---|
| Historical Data | `Backpage.aspx` |
| P/E, P/B & Div.Yield | `Backpage.aspx` |
| VIX Data | `Backpage.aspx` |
| Total Index | `Backpage.aspx` |

> To find the fragment for a new tab: open Edge DevTools → Network → filter XHR →
> manually click Submit → copy a unique substring from the POST request URL.

### Selector naming pattern

All dropdown IDs follow: `ddlHistorical` + tab-prefix + role.

| Tab | Prefix | Sub-Index ID | Index Name ID |
|---|---|---|---|
| Historical Data | `typee` | `ddlHistoricaltypeeSubindex` | `ddlHistoricaltypeeindex` |
| P/E, P/B & Div.Yield | `divtypee` | `ddlHistoricaldivtypeeSubindex` | `ddlHistoricaldivtypeeindex` |

Use this pattern to predict IDs for VIX and Total Index tabs before confirming in DevTools.

---

## Dropdown Interaction Rules

### 1. Always target dropdowns by their specific ID

Never pass `null` to scan all visible selects. Multiple dropdowns are visible simultaneously
and the wrong one will be matched.

~~~csharp
// ✅ Correct — targets exactly the right dropdown
await SelectDropdownByTextAsync(page, indexName, "#ddlHistoricaltypeeindex");

// ❌ Wrong — scans all visible selects, will hit the wrong one
await SelectDropdownByTextAsync(page, indexName, null);
~~~

### 2. Fire jQuery change event after setting a dropdown value

The site uses jQuery event handlers to trigger AJAX chains. Setting `.value` alone does nothing.
Always fire the change event after setting the value:

~~~javascript
jQuery(select).val(option.value).trigger('change');

// Fallback if jQuery is unavailable:
select.dispatchEvent(new Event('input',  { bubbles: true }));
select.dispatchEvent(new Event('change', { bubbles: true }));
~~~

### 3. Cascade order is strict — wait for AJAX between steps

- Selecting **Index Type** → triggers AJAX → populates **Sub-Index** dropdown
- Selecting **Sub-Index** → triggers AJAX → populates **Index Name** dropdown
- Selecting **Index Name** → no AJAX, UI state only

Never attempt to select a downstream dropdown before its upstream AJAX has completed.
The Index Name dropdown will be empty and selection will silently fail.

### 4. How to find which options are available in a dropdown

Run in DevTools Console after the tab and Index Type are selected:

~~~javascript
Array.from(document.querySelectorAll('select'))
    .filter(s => s.offsetParent !== null)
    .forEach(s => console.log(
        s.id,
        Array.from(s.options).map(o => o.textContent.trim())
    ));
~~~

---

## Date Input Rules

### Use the jQuery UI datepicker API — not native input methods

The date inputs are jQuery UI datepicker widgets. The Submit button reads the datepicker's
internal state, not the visible input value. Native `FillAsync` or setting `.value` directly
updates only the visible text — Submit ignores it.

~~~javascript
// ✅ Correct — updates internal datepicker state
jQuery('#datepickerFrom').datepicker('setDate', new Date(2020, 0, 1));

// ❌ Wrong — updates only visible text, Submit reads old internal state
document.querySelector('#datepickerFrom').value = '01-01-2020';
~~~

### Fallback if jQuery is unavailable

~~~javascript
const el = document.querySelector(selector);
const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
setter.call(el, value);
el.dispatchEvent(new Event('input',  { bubbles: true }));
el.dispatchEvent(new Event('change', { bubbles: true }));
el.dispatchEvent(new Event('blur',   { bubbles: true }));
~~~

### Date format

Always pass dates as `dd-MM-yyyy` (e.g. `01-01-2020`).

---

## Clicking Elements

### Use JavaScript click — not Playwright's ClickAsync

`ClickAsync` requires the element to be in the viewport and visible. Several elements on
this page (Submit, CSV link) may be scrolled out of view or inside conditionally shown
sections. JS click bypasses all visibility requirements.

~~~csharp
// ✅ Correct
await page.EvaluateAsync($"() => document.querySelector('{selector}')?.click()");

// ❌ Wrong — times out if element is off-screen or in a hidden section
await page.ClickAsync(selector);
~~~

---

## Detecting AJAX Completion

### The only reliable method: listen for the response event

Set up the listener **before** triggering the action. Race it against a timeout.
**Always check which task won** — do not ignore the return value of `Task.WhenAny`.

~~~csharp
var ajaxCompleted = new TaskCompletionSource<bool>();
EventHandler<IResponse> responseHandler = (_, response) =>
{
    if (response.Url.Contains(_ajaxUrlFragment) && response.Status == 200)
        ajaxCompleted.TrySetResult(true);
};
_page.Response += responseHandler;

// Trigger the action (dropdown change or Submit click) here

var timeoutTask = Task.Delay(15_000);
Task winner;
try
{
    winner = await Task.WhenAny(ajaxCompleted.Task, timeoutTask);
}
finally
{
    _page.Response -= responseHandler;  // always unsubscribe
}

if (winner == timeoutTask)
{
    // Server did not respond within 15 s — treat as server failure, stop the run
    // Do NOT fall through to check CSV visibility — it will always be false here
}
else
{
    await page.WaitForTimeoutAsync(750);  // allow DOM to update after response
    var csvVisible = await page.IsVisibleAsync(csvSelector);
}
~~~

### Why checking the winner is mandatory

| Scenario | AJAX responds? | CSV visible? | Winner ignored | Winner checked |
|---|---|---|---|---|
| Data exists | ✅ Yes | ✅ Yes | Download ✅ | Download ✅ |
| No data for period | ✅ Yes | ❌ No | Skip ✅ | Skip ✅ |
| Server unresponsive | ❌ Timeout | ❌ No | Silent skip ❌ | Stop run ✅ |

Ignoring the winner causes server outages to be silently logged as "no data".
Every chunk takes exactly 15 seconds and reports "Skipped" — zero downloads, zero errors.

### Never use RouteAsync for observation

`RouteAsync` intercepts requests even with `ContinueAsync`. This subtly alters timing
and breaks the AJAX chain — cascading dropdowns stop populating.
Use `page.Response` (passive observation) only.

### Never read response body in the event handler

~~~csharp
// ❌ Wrong — locks the response stream, page JS cannot read it
page.Response += async (_, r) => { var body = await r.TextAsync(); };

// ✅ Correct — observe URL and status only
page.Response += (_, r) => { if (r.Url.Contains("Backpage.aspx")) ... };
~~~

---

## Checking Whether Data Exists

The CSV link (`#exporthistorical`, `#exporthistoricaldiv`, etc.) is **always present in the DOM**.
It is never added or removed — only shown or hidden via CSS.

- After AJAX completes: check `page.IsVisibleAsync(csvSelector)`
- Visible → data exists → proceed to download
- Hidden → no data for this date range → skip silently (the site shows no error)

~~~csharp
// ✅ Correct — checks visibility
var csvVisible = await page.IsVisibleAsync("#exporthistorical");

// ❌ Wrong — always true because element always exists in DOM
await page.WaitForSelectorAsync("#exporthistorical");
~~~

---

## Page Load

### Use DOMContentLoaded, not Load

Third-party analytics scripts on this page never finish loading. `WaitUntilState.Load`
hangs indefinitely. Use `DOMContentLoaded` and swallow the timeout exception.

~~~csharp
try
{
    await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15_000 });
}
catch (TimeoutException)
{
    // Analytics still loading — page content is ready, continue
}
~~~

### Block heavy assets before navigating

Blocking images, fonts, and media speeds up the initial load significantly.

~~~csharp
await page.RouteAsync("**/*", route =>
{
    var type = route.Request.ResourceType;
    if (type == "image" || type == "media" || type == "font")
        return route.AbortAsync();
    return route.ContinueAsync();
});
~~~

---

## Anti-Bot Detection

The site detects automation via two browser signals. Without masking both, dropdowns
load slowly (10–30 seconds per change). With both masked, response is instant.

### 1. Mask navigator.webdriver

~~~csharp
await context.AddInitScriptAsync(
    "Object.defineProperty(navigator, 'webdriver', { get: () => undefined });");
~~~

### 2. Disable AutomationControlled flag — use real Edge, not bundled Chromium

~~~csharp
await playwright.Chromium.LaunchAsync(new()
{
    Channel = "msedge",
    Args    = new[] { "--disable-blink-features=AutomationControlled" }
});
~~~

---

## Quick Reference — Do and Don't

| Topic | ✅ Do | ❌ Don't |
|---|---|---|
| Page load | `WaitUntil = DOMContentLoaded` + swallow TimeoutException | `WaitUntil = Load` — hangs forever |
| Asset loading | Block images/fonts/media via RouteAsync | Load everything — slow and unnecessary |
| Observation | `page.Response` event (passive) | `page.RouteAsync` — breaks AJAX chains |
| AJAX detection | Listen for response URL + race with timeout | `NetworkIdle` — never resolves |
| Task.WhenAny | Always save and check the winner | Ignore return value — causes silent failures |
| Response body | Never read in event handler | `response.TextAsync()` in handler — locks stream |
| Dropdown targeting | Pass specific selector ID | Pass `null` — hits wrong dropdown |
| Dropdown change | Fire jQuery `trigger('change')` | Set `.value` only — AJAX chain won't fire |
| Date input | jQuery `datepicker('setDate', ...)` | `FillAsync` or `.value =` — Submit ignores it |
| Clicking | `element.click()` via JS | `page.ClickAsync` — times out on off-screen elements |
| CSV detection | `page.IsVisibleAsync` | `WaitForSelectorAsync` — always resolves, element always exists |
| Browser | Real Edge + anti-bot flags masked | Bundled Chromium — detected, dropdowns throttled |
