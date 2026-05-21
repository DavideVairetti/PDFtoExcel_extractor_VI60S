namespace EstrattoreContatori.Services;

public sealed class LogService : IDisposable
{
    private readonly StreamWriter _writer;
    private bool _disposed;

    public LogService(string logPath)
    {
        LogPath = logPath;
        var directory = Path.GetDirectoryName(logPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var stream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream) { AutoFlush = true };
    }

    public string LogPath { get; }

    public void Info(string message) => Write("INFO", message);

    public void Warning(string message) => Write("WARN", message);

    public void Error(string message, Exception exception)
    {
        Write("ERROR", message);
        Write("ERROR", exception.ToString());
    }

    private void Write(string level, string message)
    {
        if (_disposed)
        {
            return;
        }

        _writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _writer.Dispose();
        _disposed = true;
    }
}
