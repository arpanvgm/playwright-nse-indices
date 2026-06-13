namespace IndexCsvConsolidator.Services;

public class LogService : IDisposable
{
    private readonly StreamWriter _writer;
    private bool _disposed;

    public LogService(string logFolder)
    {
        Directory.CreateDirectory(logFolder);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string logPath = Path.Combine(logFolder, $"run_{timestamp}.log");
        _writer = new StreamWriter(logPath, append: false) { AutoFlush = true };
        WriteLine("INFO ", $"=== IndexCsvConsolidator started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
    }

    public void Info(string message) => WriteLine("INFO ", message);

    public void Warning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        WriteLine("WARN ", message);
        Console.ResetColor();
    }

    public void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        WriteLine("ERROR", message);
        Console.ResetColor();
    }

    public void Conflict(string message)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        WriteLine("CONF ", message);
        Console.ResetColor();
    }

    public void Summary(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        WriteLine("SUMM ", message);
        Console.ResetColor();
    }

    private void WriteLine(string level, string message)
    {
        string line = $"[{level}] {DateTime.Now:HH:mm:ss}  {message}";
        Console.WriteLine(line);
        _writer.WriteLine(line);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        WriteLine("INFO ", $"=== IndexCsvConsolidator ended at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        _writer.Dispose();
    }
}
