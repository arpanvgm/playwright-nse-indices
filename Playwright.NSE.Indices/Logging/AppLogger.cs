namespace Playwright.NSE.Indices.Logging;

/// <summary>
/// Writes timestamped log lines to a run log file.
/// Console output is handled separately by existing Console.WriteLine calls.
/// Each run produces its own file named run_YYYYMMDD_HHmmss.log,
/// written next to the executable.
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
        _writer.WriteLine($"[{Now()}] Run started");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Write an informational line to the log file only.</summary>
    public void Info(string message)
    {
        _writer.WriteLine($"[{Now()}] {message}");
    }

    /// <summary>Write a warning line to the log file only.</summary>
    public void Warn(string message)
    {
        _writer.WriteLine($"[{Now()}] [WARN] {message}");
    }

    /// <summary>Write an error line to the log file only.</summary>
    public void Error(string message)
    {
        _writer.WriteLine($"[{Now()}] [ERROR] {message}");
    }

    /// <summary>Write an error with full exception detail to the log file only.</summary>
    public void Error(string message, Exception ex)
    {
        _writer.WriteLine($"[{Now()}] [ERROR] {message}");
        _writer.WriteLine($"[{Now()}] [ERROR] {ex.GetType().Name}: {ex.Message}");
        _writer.WriteLine($"[{Now()}] [ERROR] {ex.StackTrace}");
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _writer.WriteLine($"[{Now()}] Run ended");
        _writer.Dispose();
        _disposed = true;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
}
