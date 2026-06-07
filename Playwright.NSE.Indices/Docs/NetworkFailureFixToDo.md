# TODO: Distinguish Network Failure from No-Data Period

## Status
**Deferred** — to be implemented after modularization is stable.

---

## The Bug

In `DownloadOrchestrator.ProcessChunksAsync`, after clicking Submit the code races
an AJAX response listener against a 15-second timeout:

~~~csharp
await Task.WhenAny(ajaxCompleted.Task, Task.Delay(15_000));
await _page.WaitForTimeoutAsync(500);
var csvVisible = await _page.IsVisibleAsync(_activeSelectors.Csv);
~~~

`Task.WhenAny` resolves when **either** condition wins.
The code does **not check which one won**.

### Consequence

| Scenario | AJAX responds? | CSV visible? | Current behaviour | Correct behaviour |
|---|---|---|---|---|
| Data exists | ✅ Yes (1–3 s) | ✅ Yes | Download ✅ | Download |
| No data for period | ✅ Yes (1–3 s) | ❌ No | Skip ✅ | Skip |
| Network / server failure | ❌ No (timeout) | ❌ No | **Silent skip** ❌ | Retry, then FAILED |

A genuine network failure is silently treated as "no data". Data is lost with no warning.

---

## Proposed Fix

Track which task won:

~~~csharp
var timeoutTask   = Task.Delay(15_000);
var winner        = await Task.WhenAny(ajaxCompleted.Task, timeoutTask);

if (winner == timeoutTask)
{
    // AJAX never responded — this is a network/server failure, NOT a no-data period
    // → apply retry logic (see below)
}
else
{
    // AJAX responded — CSV visibility is the correct no-data signal
    var csvVisible = await _page.IsVisibleAsync(_activeSelectors.Csv);
    if (!csvVisible) { /* skip — no data */ }
    else             { /* download */       }
}
~~~

---

## Proposed Retry Behaviour

- On AJAX timeout: **retry the same chunk up to 2 more times**
- Delay between retries: `DelayMs * 3`
- If all 3 attempts time out: log `[ERROR] FAILED after 3 attempts` with index name
  and date range, increment `TotalFailed`, and **continue to the next chunk**
  (do not abort the whole run — one bad chunk should not kill 200 others)

~~~csharp
const int maxAttempts = 3;
for (int attempt = 1; attempt <= maxAttempts; attempt++)
{
    var timeoutTask = Task.Delay(15_000);
    var winner      = await Task.WhenAny(ajaxCompleted.Task, timeoutTask);

    if (winner != timeoutTask)
        break; // AJAX responded — proceed normally

    if (attempt < maxAttempts)
    {
        logger.Warn($"AJAX timeout (attempt {attempt}/{maxAttempts}) — retrying after delay...");
        await _page.WaitForTimeoutAsync(delayMs * 3);
        // re-attach handler and re-click Submit here
    }
    else
    {
        logger.Error($"FAILED after {maxAttempts} attempts: {indexName} | {fromStr} to {toStr}");
        failed++;
        goto nextChunk;
    }
}
~~~

---

## Where to Implement

`Playwright.NSE.Indices/Downloading/DownloadOrchestrator.cs`
Method: `ProcessChunksAsync`

The retry loop should wrap the block from `SetDateAsync` through `IsVisibleAsync`.
The AJAX listener setup and handler cleanup must be inside the retry loop.

---

## Log File Benefit

Once the file logger (`AppLogger`) is in place, every `[ERROR]` line includes:
- Timestamp
- Index name
- Date range (From → To)
- Attempt count
- Exception message + stack trace (if any)

This makes failures easy to grep and re-run manually.
