using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Input;

namespace Kadr.Desktop.Services.Settings;

public sealed class SettingsApplication : ISettingsApplication
{
    private readonly SettingsStore _store;
    private readonly GlobalHotkeyService _hotkey;
    private readonly ICaptureService _capture;
    private readonly AutoStartService _autoStart;

    public SettingsApplication(SettingsStore store, GlobalHotkeyService hotkey, ICaptureService capture, AutoStartService autoStart)
    {
        _store = store;
        _hotkey = hotkey;
        _capture = capture;
        _autoStart = autoStart;
    }

    public async Task ApplyAsync(AppSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if ((settings.Backend == CaptureBackend.WindowsGraphicsCapture) && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            throw new PlatformNotSupportedException("Windows Graphics Capture requires Windows 10 version 2004 in Kadr.");
        }

        string? shortcutError = ShortcutPolicy.GetError(settings);
        if (shortcutError is not null)
        {
            throw new ArgumentException(shortcutError, nameof(settings));
        }

        AppSettings previous = _store.Current;
        await _hotkey.SetAsync(ShortcutCatalog.GetBindings(settings, EditorAction.Capture), ct).ConfigureAwait(false);

        try
        {
            _autoStart.SetEnabled(settings.AutoStart);
            await _store.SaveAsync(settings, ct).ConfigureAwait(false);
        }
        catch
        {
            await _hotkey.SetAsync(ShortcutCatalog.GetBindings(previous, EditorAction.Capture), CancellationToken.None).ConfigureAwait(false);
            _autoStart.SetEnabled(previous.AutoStart);
            throw;
        }

        await _capture.ConfigureAsync(settings.Backend, settings.Mode, ct).ConfigureAwait(false);
    }
}
