using Microsoft.Extensions.Logging;

namespace Kadr.Desktop.Services;

internal sealed class FileLogger : ILogger
{
    internal const LogLevel MinimumLevel = LogLevel.Error;

    private readonly string _category;
    private readonly Action<string> _write;

    internal FileLogger(string category, Action<string> write)
    {
        _category = category;
        _write = write;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= MinimumLevel && logLevel < LogLevel.None;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
        {
            _write($"{DateTime.UtcNow:O} [{logLevel}] {_category}: {formatter(state, exception)} {exception}");
        }
    }
}
