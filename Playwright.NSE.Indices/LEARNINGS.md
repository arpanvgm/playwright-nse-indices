# Learnings & Debug Knowledge — Playwright & Site Behavior

Technical discoveries about automating data downloads on this site. Focus: what works, what doesn't, why.

---

## The Core Challenge

**Detecting when async data has loaded and is ready to download.**

The site is SPA-like: Submit triggers AJAX, data loads asynchronously. We need to know when AJAX is done so we can check if the CSV link appeared (data exists) or stay hidden (no data).

---

## ✅ Solution: Listen for AJAX Response (RECOMMENDED)

### Implementation

// Set up response listener BEFORE triggering the AJAX.
// Use _ajaxUrlFragment (from ReportSelectors) — NOT a hardcoded string.
// Each report tab posts to a different endpoint; using the wrong fragment means
// the TaskCompletionSource never fires and every chunk silently times out.
var ajaxCompleted = new TaskCompletionSource<bool>();
EventHandler<IResponse> responseHandler = (_, response) =>
{
    if (response.Url.Contains(_ajaxUrlFragment) && response.Status == 200)
        ajaxCompleted.TrySetResult(true);
};
page.Response += responseHandler;

// Click the button that triggers AJAX
await JsClick(page, SUBMIT_SEL);

// Race: AJAX response vs timeout — ALWAYS save the winner
var timeoutTask = Task.Delay(15_000);
Task winner;
try
{
    winner = await Task.WhenAny(ajaxCompleted.Task, timeoutTask);
}
finally
{
    page.Response -= responseHandler;  // always clean up
}

// Check who won — this is critical
if (winner == timeoutTask)
{
    // Server did not respond — this is a server failure, NOT a no-data period
    // Do NOT fall through to csvVisible check — it will always be false here
    // and will silently misreport a server outage as "no data"
}
else
{
    // AJAX completed — now CSV visibility is a reliable signal
    await page.WaitForTimeoutAsync(750);  // DOM update buffer
    var visible = await page.IsVisibleAsync(CSV_SEL);
}

### Why Checking the Winner Matters

| Scenario | AJAX responds? | CSV visible? | If winner not checked | If winner checked |
|---|---|---|---|---|
| Data exists | ✅ Yes (1–3 s) | ✅ Yes | Download ✅ | Download ✅ |
| No data for period | ✅ Yes (1–3 s) | ❌ No | Skip ✅ | Skip ✅ |
| Server unresponsive | ❌ No (timeout) | ❌ No | **Silent skip** ❌ | Stop run ✅ |

**Real-world symptom of the bug:** every chunk takes exactly 15–16 seconds and logs
`Skipped` — even for indices that definitely have data. The server was down, but the
code was misreading timeouts as no-data responses.

### Why This Works

| Aspect | Benefit |
|--------|---------|
| **Direct signal** | AJAX response = data processing is done. No guessing. |
| **Server speed agnostic** | Slow server? We wait. Fast server? We proceed. Automatic. |
| **No DOM tricks** | Doesn't depend on visibility, CSS, or element state. Pure network signal. |
| **Passive observation** | Listening to response event doesn't interfere with page's own JS. |
| **Consistent timing** | Both "data found" and "no data" scenarios resolve in 1–3 seconds. |

---

## ❌ What NOT to Do

### 1. NetworkIdle (Unreliable)

await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });

**Problem:** Analytics, tracking, and third-party scripts keep firing indefinitely. Network never truly "idle".

**Result:** Timeout waits or fragile timing.

---

### 2. WaitForSelectorAsync(Visible) on Always-Present Elements

await page.WaitForSelectorAsync(CSV_SEL, new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });

**Problem:** The CSV link (`#exporthistoricaldiv`) is always in the DOM. It's just hidden/shown via CSS.

**Expected:** Element becomes visible (AJAX completes, data loads).
**Reality:** Element already exists but hidden. Wait never resolves until timeout.
**Impact:** 20-second waste when no data exists.

---

### 3. Pre-Manipulating DOM with Inline Styles

await page.EvaluateAsync($@"() => {{
    document.querySelector('{CSV_SEL}').style.display = 'none';
}}");

**Problem:** Inline `style.display:none` has higher specificity than CSS classes. Site's JS can't easily override it.

**Result:** Even when data loads, CSV stays hidden because our inline style blocks it.

---

### 4. RouteAsync for Observation (Breaks Page)

await page.RouteAsync("**/Backpage.aspx", route => { /* observe */ route.ContinueAsync(); });

**Problem:** Even with `ContinueAsync()`, request interception subtly alters timing. AJAX chains break.

**Result:** Cascading dropdowns stop loading, AJAX calls fail.

**Rule:** Use `page.RouteAsync` ONLY if you need to modify/block requests. Never for observation.

---

### 5. Reading Response Body in Event Handler

page.Response += async (_, response) =>
{
    var body = await response.TextAsync();  // ❌ LOCKS response
};

**Problem:** `response.TextAsync()` consumes the response stream. Locks it from the page's own JavaScript.

**Result:** Page can't read the response data. AJAX handlers break.

**Rule:** Use response listener ONLY for metadata (URL, status). Never call `TextAsync()` in the handler.

---

### 6. Not Checking the Winner of Task.WhenAny

// ❌ Wrong — ignores which task won
await Task.WhenAny(ajaxCompleted.Task, Task.Delay(15_000));
var csvVisible = await page.IsVisibleAsync(CSV_SEL);

**Problem:** If the timeout wins (server down), `csvVisible` will be false and the chunk
is silently logged as "no data". A server outage becomes invisible in the output.

**Result:** Entire runs complete with zero downloads and zero errors — all silently skipped.

**Rule:** Always save the return value of `Task.WhenAny` and check which task won before
deciding what the result means.

---

### 7. Hardcoding AJAX URL Fragments

// ❌ Wrong — only works for Historical Data tab
if (response.Url.Contains("Backpage.aspx") && response.Status == 200)

**Problem:** Each report tab posts to a different server-side endpoint. The PE tab
(`P/E, P/B & Div.Yield`) uses a completely different URL from `Backpage.aspx`.
Hardcoding the wrong fragment means `ajaxCompleted` never fires, the 15-second
timeout wins every single chunk, and all chunks are silently misreported as "no data".

**Result:** All chunks skip instantly (or after timeout). Zero downloads. Zero errors logged.

**Rule:** Always use `_ajaxUrlFragment` sourced from `ReportSelectors.AjaxUrlFragment`.
Never hardcode a URL fragment in `DownloadOrchestrator`.

---

### 8. Passing null as the Index Name Dropdown Selector

// ❌ Wrong — scans ALL visible selects, hits the wrong one
var selected = await SelectDropdownByTextAsync(page, indexName, null);

**Problem:** On the PE tab there are multiple visible dropdowns. Passing `null` scans all
of them and may match the wrong one (e.g. a category dropdown that happens to have an
option with the same text), or fail to match at all if the index name dropdown isn't the
first visible select encountered.

**Rule:** Always pass the report-specific `IndexNameDropdown` selector from `ReportSelectors`.
This targets exactly the right `<select>` regardless of what else is visible on the page.

---

## 📍 Site Behavior: When Data Loads

### Sequence (Data Available)
1. **User selects dates** (or program via `SetDate`)
2. **User clicks Submit** (or program via `JsClick`)
3. **AJAX fires** → POST to report-specific endpoint
4. **Server responds** (1–3 seconds typically)
5. **Page's JS processes response** → updates DOM
6. **Data table renders** with rows
7. **Index Name displays**
8. **CSV link appears** (becomes visible)

### No Data Scenario
1. **Same steps 1–4**
2. **Server responds** but with empty/no-data indicator
3. **Page's JS processes response** → doesn't render table
4. **CSV link stays hidden** (no change from initial state)
5. **No error message** — just silent absence

### Key Insight
The site doesn't show an error for "no data". It simply doesn't render the CSV link.
**The CSV link's visibility IS the data availability signal — but only when AJAX actually completed.**
If the AJAX timed out, CSV visibility tells you nothing.

---

## DOM & Element Visibility Challenges

### CSV Link is Always in DOM

<a href="#" id="exporthistoricaldiv">csv format</a>

- Always present in HTML
- Hidden by default via CSS class/style
- Made visible by site's JS when data loads
- **Problem:** `WaitForSelectorAsync` (checking existence) passes immediately because element exists

### Visibility Check vs Existence Check

// ❌ Wrong: checks if element exists (always true)
await page.WaitForSelectorAsync(CSV_SEL, new() { Timeout = 20_000 });

// ✅ Right: checks if element is visible (what we actually want)
var isVisible = await page.IsVisibleAsync(CSV_SEL);

### offsetParent Check in JavaScript

element.offsetParent === null  // element is hidden (display:none, visibility:hidden, or ancestor hidden)
element.offsetParent !== null  // element is visible

Use this in custom JS for manual visibility checks.

---

## Confirmed Selector IDs by Report Tab

Confirmed from live DOM inspection (DevTools → Console → querySelectorAll('select')).

| Report Tab | Index Type Dropdown | Sub-Index Dropdown | Index Name Dropdown |
|---|---|---|---|
| P/E, P/B & Div.Yield | `#ddlHistoricaldivtypee` | `#ddlHistoricaldivtypeeSubindex` | `#ddlHistoricaldivtypeeindex` |
| Historical Data | `#ddlHistoricaltypee` | `#ddlHistoricaltypeeSubindex` | `#ddlHistoricaltypeeindex` |
| VIX Data | — | `#ddlHistoricalvixsubindex` | `#ddlHistoricalvixindex` |
| Total Index | — | `#ddlHistoricaltotalsubindex` | `#ddlHistoricaltotalindex` |

**Note:** The PE tab uses a completely separate set of `<select>` elements from the
Historical Data tab. They coexist in the DOM simultaneously — the inactive tab's
dropdowns are hidden (`offsetParent === null`). This is why the smart scanner must
check visibility before matching options.

### How to find selectors for a new report tab

Run this in DevTools Console after selecting the report tab manually:

~~~javascript
Array.from(document.querySelectorAll('select'))
    .filter(s => s.offsetParent !== null)
    .forEach(s => console.log(s.id, Array.from(s.options).map(o => o.textContent.trim())));
~~~

---

## AJAX URL Fragments by Report Type

Each report tab posts to a different server-side endpoint. Using the wrong
URL fragment means the AJAX listener never fires, the timeout wins every time,
and every chunk is silently misreported as "no data".

| Report Tab | AjaxUrlFragment |
|---|---|
| P/E, P/B & Div.Yield | `Backpage.aspx` |
| Historical Data | `Backpage.aspx` |
| VIX Data | `Backpage.aspx` |
| Total Index | `Backpage.aspx` |

These values live in `Program.cs` inside the `knownReports` array.

**How to find the correct fragment for a new report type:**
1. Open Edge DevTools → Network tab
2. Manually select an index, set dates, and click Submit
3. Watch for the XHR/Fetch request that returns the data (status 200, not a static asset)
4. Copy a unique substring from that request URL and use it as `AjaxUrlFragment`

---

## Date Setting: jQuery Datepicker

### Native Input Methods Don't Work

// ❌ This doesn't work:
await page.FillAsync(selector, "01-01-2020");

**Why:** jQuery UI datepicker maintains internal date state separate from the input's visible value. Submit button reads the internal state, not the input value.

### Correct Approach: Use jQuery API

await page.EvaluateAsync($@"() => {{
    jQuery('#datepickerFromDivYield').datepicker('setDate', new Date(2020, 0, 1));
}}");

**Why:** Updates both the internal state (what Submit reads) and the visual input.

### Fallback (If jQuery Unavailable)

// Set value + fire events
const el = document.querySelector(selector);
el.value = '01-01-2020';
el.dispatchEvent(new Event('input', { bubbles: true }));
el.dispatchEvent(new Event('change', { bubbles: true }));
el.dispatchEvent(new Event('blur', { bubbles: true }));

Less reliable but works on non-jQuery inputs.

---

## Clicking Elements: JS Click vs ClickAsync

### ClickAsync Issues

await page.ClickAsync(selector);  // ❌ Can timeout if element off-screen or in hidden section

**Problem:** Playwright's `ClickAsync` waits for element to be in viewport, visible, and stable. Elements in hidden sections cause 30-second timeout.

### JS Click (Bypasses Visibility Requirement)

await page.EvaluateAsync($@"() => {{
    document.querySelector('{selector}')?.click();
}}");

**Benefit:** Clicks element regardless of visibility, scroll position, or section state.

**Use case:** When clicking elements that might be scrolled out of view or in hidden DOM sections.

---

## Timeout Strategy: Task.WhenAny Racing

### Problem: Sequential Timeouts Waste Time

// ❌ Waits for both, cumulative: could be 20+ seconds
await page.WaitForSelectorAsync(..., new() { Timeout = 10_000 });
await page.WaitForSelectorAsync(..., new() { Timeout = 10_000 });

### Solution: Race Multiple Conditions

// ✅ Resolves when EITHER completes — and always check the winner
var timeoutTask   = Task.Delay(15_000);
var winner        = await Task.WhenAny(ajaxCompleted.Task, timeoutTask);

if (winner == timeoutTask)
    // handle failure
else
    // handle success

**Benefit:** Both "success" and "failure" paths resolve quickly without waiting for timeouts.

---

## Page Load Timeouts from Third-Party Scripts

### GotoAsync Hangs Indefinitely

// ❌ This times out — analytics never finish loading
await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.Load });

### Solution: DOMContentLoaded + Exception Swallow

// ✅ DOMContentLoaded fires quickly, content is ready
try
{
    await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
}
catch (TimeoutException)
{
    // Analytics still loading — that's fine, page content is ready
}

**Why:** Google Analytics, Tag Manager, and other third-party scripts sometimes never finish. `DOMContentLoaded` fires when the page's own content is ready, ignoring third-party hangers.

---

## Browser Detection & Anti-Bot Measures

### Two Flags Give Away Automation

**1. navigator.webdriver**

// ❌ Sites detect this:
navigator.webdriver === true

**Fix:**

await context.AddInitScriptAsync(
    "Object.defineProperty(navigator, 'webdriver', { get: () => undefined });");

**2. AutomationControlled Chrome Flag**

// ❌ Chromium bundled with Playwright launches with this flag

**Fix:**

await playwright.Chromium.LaunchAsync(new()
{
    Channel = "msedge",  // Use real installed Edge, not bundled Chromium
    Args = new[] { "--disable-blink-features=AutomationControlled" }
});

**Combined effect:** Browser becomes indistinguishable from manual user in Edge.

**Note:** Without these fixes, the site throttles dropdown loading (10–30 seconds per dropdown). With both, instant response.

---

## Passive Event Listeners (Safe)

### What's Safe

page.Request += (_, request) =>
{
    Console.WriteLine(request.Url);  // ✅ Safe
};

page.Response += (_, response) =>
{
    if (response.Status == 200)  // ✅ Safe
        Console.WriteLine("Success");
};

**Why:** Pure observation. Doesn't alter request/response.

### What's Unsafe

page.Response += async (_, response) =>
{
    var body = await response.TextAsync();  // ❌ Unsafe — locks response
};

---

## Summary: Core Principles

| Principle | Reason |
|-----------|--------|
| **Listen for AJAX response, not DOM state** | Direct signal; handles variable server speed |
| **Always check the winner of Task.WhenAny** | Timeout ≠ no data; server down must be detected |
| **Use passive observation, not interception** | Doesn't break page's own JavaScript |
| **Avoid pre-manipulating DOM** | Fights with site's CSS and JS |
| **JS click instead of ClickAsync for hidden elements** | Bypasses visibility requirements |
| **DOMContentLoaded instead of Load** | Third-party scripts don't block |
| **Mask webdriver and AutomationControlled** | Site treats you like a human user |
| **Task.WhenAny for racing conditions** | Resolves quickly without timeout waste |
| **Never hardcode AJAX URL fragments** | Each report tab has its own endpoint; use `ReportSelectors.AjaxUrlFragment` |
| **Always pass IndexNameDropdown to SelectDropdownByTextAsync** | Prevents matching wrong visible dropdown on page |

---

## What Worked in This Automation

✅ AJAX response listener (page.Response event)
✅ Checking Task.WhenAny winner to distinguish timeout from no-data
✅ Passive observation only (no RouteAsync)
✅ JS-based clicking for off-screen elements
✅ CSV visibility as "data ready" signal (only after confirmed AJAX completion)
✅ jQuery datepicker API for date setting
✅ DOMContentLoaded with TimeoutException catch
✅ Real Edge browser with anti-bot flags masked
✅ Per-report IndexNameDropdown selector (prevents wrong dropdown selection)
✅ Per-report AjaxUrlFragment (prevents silent timeout-as-no-data misreporting)

---

## What Didn't Work

❌ NetworkIdle (third-party requests interfere)
❌ WaitForSelectorAsync on always-present elements
❌ Pre-hiding with inline styles
❌ RouteAsync for observation
❌ Reading response.TextAsync() in event handler
❌ ClickAsync on off-screen elements
❌ Bundled Chromium without anti-bot fixes
❌ Task.WhenAny without checking the winner (silent server failures)
❌ Hardcoded "Backpage.aspx" URL fragment (wrong for PE tab, silent failure)
❌ null selector for index name dropdown (matches wrong visible select on PE tab)
