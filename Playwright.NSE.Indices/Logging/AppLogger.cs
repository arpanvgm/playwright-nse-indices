namespace Playwright.NSE.Indices.Logging;

/// <summary>
/// Writes timestamped log lines to both the console and a run log file.
/// The log file is created in the application's working directory and named
/// run_YYYYMMDD_HHmmss.log so each run produces its own file.
///
/// All existing Console.WriteLine calls are left untouched in their original
/// locations. Call AppLogger methods for lines that must also appear in the log.
/// </summary>
public sealed class AppLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private bool _disposed;

    public string LogFilePath { get; }

    public AppLogger()
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        LogFilePath   = Path.Combine(AppContext.BaseDirectory, $"run_{timestamp}.log");
        _writer       = new StreamWriter(LogFilePath, append: false) { AutoFlush = true };
        _writer.WriteLine($"[{Now()}] [INFO ] Log started — {LogFilePath}");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Informational line — written to console AND log file.</summary>
    public void Info(string message)
    {
        Console.WriteLine(message);
        _writer.WriteLine($"[{Now()}] [INFO ] {message}");
    }

    /// <summary>
    /// Write to log file only (no console output).
    /// Use for lines that are already printed by existing Console.WriteLine calls.
    /// </summary>
    public void LogOnly(string message)
    {
        _writer.WriteLine($"[{Now()}] [INFO ] {message}");
    }

    /// <summary>Warning line — written to console AND log file.</summary>
    public void Warn(string message)
    {
        Console.WriteLine(message);
        _writer.WriteLine($"[{Now()}] [WARN ] {message}");
    }

    /// <summary>Error line — written to console AND log file with [ERROR] tag.</summary>
    public void Error(string message)
    {
        Console.WriteLine(message);
        _writer.WriteLine($"[{Now()}] [ERROR] {message}");
    }

    /// <summary>Error with exception detail — console shows short message, log gets full detail.</summary>
    public void Error(string message, Exception ex)
    {
        Console.WriteLine(message);
        _writer.WriteLine($"[{Now()}] [ERROR] {message}");
        _writer.WriteLine($"[{Now()}] [ERROR] Exception: {ex.GetType().Name}: {ex.Message}");
        _writer.WriteLine($"[{Now()}] [ERROR] StackTrace: {ex.StackTrace}");
    }

    /// <summary>Blank separator line — written to log file only.</summary>
    public void Separator()
    {
        _writer.WriteLine();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _writer.WriteLine($"[{Now()}] [INFO ] Log closed.");
        _writer.Dispose();
        _disposed = true;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
}
