using System.Text.Json;
using Kadr.Desktop.Services.Editing;
using Microsoft.Extensions.Logging;

namespace Kadr.Desktop.Services.Settings;

public sealed class SettingsStore
{
    public AppSettings Current { get; private set; } = new();
    public bool Exists => File.Exists(_path);
    public event EventHandler? Changed;

    private readonly ILogger<SettingsStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly string _path;
    private SettingsDocument? _document;
    private Exception? _loadFailure;
    private bool _isLoaded;

    public SettingsStore(ILogger<SettingsStore> logger)
        : this(logger, ApplicationDataPaths.Default.SettingsFile)
    {
    }

    public SettingsStore(ILogger<SettingsStore> logger, string path)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ReadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            _loadFailure = exception;
            _logger.LogError(exception, "Settings could not be loaded; the existing file is protected from writes");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Task SaveAsync(AppSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? error = ShortcutPolicy.GetError(settings);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(settings));
        }
        return UpdateAsync(current => settings, ct);
    }

    public async Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(update);
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (_loadFailure is not null)
            {
                throw new InvalidOperationException("The existing settings could not be loaded and cannot be overwritten.", _loadFailure);
            }

            if (!_isLoaded)
            {
                await ReadAsync(ct).ConfigureAwait(false);
            }

            AppSettings settings = update(Current);
            Validate(settings);
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Settings directory is missing."));
            string temporary = _path + ".tmp";
            string json = (_document ?? new SettingsDocument("{}")).Serialize(settings);
            await File.WriteAllTextAsync(temporary, json, ct).ConfigureAwait(false);
            File.Move(temporary, _path, true);
            _document = new SettingsDocument(json);
            Current = settings;
        }
        finally
        {
            _writeLock.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ReadAsync(CancellationToken ct)
    {
        if (File.Exists(_path))
        {
            string json = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
            SettingsDocument document = new(json);
            AppSettings settings = document.Read();
            Validate(settings);
            _document = document;
            Current = settings;
        }

        _loadFailure = null;
        _isLoaded = true;
    }

    private static void Validate(AppSettings settings)
    {
        if (!Enum.IsDefined(settings.Backend) || !Enum.IsDefined(settings.Mode) || !Enum.IsDefined(settings.CopyFormat)
            || !Enum.IsDefined(settings.ColorPickerTab)
            || !Enum.IsDefined(settings.OutsideSelectionBehavior)
            || settings.HiddenToolbarItems is null || settings.HiddenToolbarItems.Any(item => !Enum.IsDefined(item))
            || (settings.EditorShortcuts is null) || settings.EditorShortcuts.Keys.Any(key => !Enum.IsDefined(key))
            || !double.IsFinite(settings.BlurStrength) || settings.BlurStrength < DrawingDefaults.MinimumBlurStrength
            || settings.BlurStrength > DrawingDefaults.MaximumBlurStrength
            || !double.IsFinite(settings.Thickness) || (settings.Thickness < DrawingDefaults.MinimumThickness)
            || (settings.Thickness > DrawingDefaults.MaximumThickness)
            || (settings.OcrLanguages is null) || (settings.OcrLanguages.Length == 0)
            || (settings.CaptureShortcut is null) || (settings.CursorShortcut is null)
            || (settings.UndoShortcut is null) || (settings.RedoShortcut is null)
            || (ShortcutPolicy.GetStructureError(settings) is not null))
        {
            throw new ArgumentException("Settings contain unsupported values.", nameof(settings));
        }
    }
}
