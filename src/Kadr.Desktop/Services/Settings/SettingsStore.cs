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

    public SettingsStore(ILogger<SettingsStore> logger)
        : this(logger, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kadr", "settings.json"))
    {
    }

    public SettingsStore(ILogger<SettingsStore> logger, string path)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            if (File.Exists(_path))
            {
                string json = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
                AppSettings candidate = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                using JsonDocument document = JsonDocument.Parse(json);
                if ((document.RootElement.ValueKind == JsonValueKind.Object)
                    && !document.RootElement.TryGetProperty(nameof(AppSettings.EditorShortcuts), out _)
                    && (candidate.CursorShortcut == new ShortcutGesture(0x48, ShortcutModifiers.None)))
                {
                    candidate = candidate with
                    {
                        CursorShortcut = ShortcutCatalog.GetDefault(EditorAction.Cursor)
                    };
                }
                if (!document.RootElement.TryGetProperty(nameof(AppSettings.AlternativeShortcuts), out _)
                    && candidate.EditorShortcuts.GetValueOrDefault(EditorAction.Select) == new ShortcutGesture(0x56, ShortcutModifiers.None))
                {
                    candidate = candidate with
                    {
                        EditorShortcuts = candidate.EditorShortcuts.Where(pair => pair.Key != EditorAction.Select).ToDictionary()
                    };
                }
                Validate(candidate);
                Current = candidate;
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException)
        {
            _logger.LogWarning(exception, "Settings could not be loaded; defaults are active");
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
            AppSettings settings = update(Current);
            Validate(settings);
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Settings directory is missing."));
            string temporary = _path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
            File.Move(temporary, _path, true);
            Current = settings;
        }
        finally
        {
            _writeLock.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
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
