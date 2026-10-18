using Microsoft.Extensions.Logging;

namespace Kadr.Desktop.Services;

internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();

    internal FileLoggerProvider(string? directory = null)
    {
        directory ??= ApplicationDataPaths.Default.LogsDirectory;
        Directory.CreateDirectory(directory);
        _writer = new StreamWriter(new FileStream(Path.Combine(directory, DateTime.UtcNow.ToString("yyyy-MM-dd") + ".log"),
            FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(categoryName, Write);
    }

    public void Dispose()
    {
        _writer.Dispose();
    }

    private void Write(string message)
    {
        lock (_gate)
        {
            _writer.WriteLine(message);
        }
    }
}
