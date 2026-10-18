using Kadr.Desktop.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class FileLoggerTests
{
    [Theory]
    [InlineData(LogLevel.Trace, false)]
    [InlineData(LogLevel.Debug, false)]
    [InlineData(LogLevel.Information, false)]
    [InlineData(LogLevel.Warning, false)]
    [InlineData(LogLevel.Error, true)]
    [InlineData(LogLevel.Critical, true)]
    [InlineData(LogLevel.None, false)]
    public void Log_SeverityThreshold_FormatsAndWritesOnlyErrorsAndCritical(LogLevel level, bool expected)
    {
        List<string> messages = [];
        FileLogger logger = new("Test", messages.Add);
        bool formatted = false;

        logger.Log(level, new EventId(1), "message", null, (state, exception) =>
        {
            formatted = true;
            return state;
        });

        Assert.Equal(expected, logger.IsEnabled(level));
        Assert.Equal(expected, formatted);
        Assert.Equal(expected ? 1 : 0, messages.Count);
    }
}
