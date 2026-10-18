using Kadr.Desktop.Services.Settings;

namespace Kadr.Desktop.Tests;

internal sealed class TestSettingsApplication : ISettingsApplication
{
    internal AppSettings? AppliedSettings
    {
        get; private set;
    }

    public Task ApplyAsync(AppSettings settings, CancellationToken ct)
    {
        AppliedSettings = settings;
        return Task.CompletedTask;
    }
}
