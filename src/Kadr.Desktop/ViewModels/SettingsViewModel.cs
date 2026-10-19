using Microsoft.Extensions.Logging;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Krivodeling.Localization.Avalonia;

using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Ocr;
using Kadr.Desktop.Services.Settings;

namespace Kadr.Desktop.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    public IReadOnlyList<OcrLanguageViewModel> Languages
    {
        get;
    }
    public bool IsWgcAvailable => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);
    public bool CanToggleCapturedCursor { get; }
    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);
    public IReadOnlyList<ShortcutGesture> ExitGestures => ShortcutCatalog.GetBindings(_store.Current, EditorAction.Cancel);
    public IReadOnlyList<ShortcutBindingViewModel> Shortcuts
    {
        get;
    }
    public ShortcutBindingViewModel ExitShortcut => Shortcuts.Single(shortcut => shortcut.Action == EditorAction.Cancel);
    public string ExitImmediatelyLabel => UiText.Format("Kadr.ExitImmediatelyFormat", ExitShortcut.DisplayName);
    public IReadOnlyList<LocalizationOption> InterfaceLanguages { get; }
    public IReadOnlyList<LocalizedOptionViewModel> CaptureModes { get; } = new LocalizedOptionViewModel[]
    {
        new("Kadr.ContinuousMode"), new("Kadr.OnDemandMode")
    };
    public IReadOnlyList<LocalizedOptionViewModel> CopyFormats { get; } = new LocalizedOptionViewModel[]
    {
        new("Kadr.PngFormat"), new("Kadr.JpegFormat")
    };
    public IReadOnlyList<LocalizedOptionViewModel> OutsideSelectionModes { get; } = new LocalizedOptionViewModel[]
    {
        new("Kadr.OutsideResize"), new("Kadr.OutsideReselect")
    };
    public bool IsGeneralSection => Section == SettingsSection.General;
    public bool IsEditorSection => Section == SettingsSection.Editor;
    public bool IsShortcutsSection => Section == SettingsSection.Shortcuts;
    public bool IsRecognitionSection => Section == SettingsSection.Recognition;
    public bool IsToolbarSection => Section == SettingsSection.Toolbar;
    public IReadOnlyList<ToolbarItemViewModel> ToolbarItems { get; }
    public IReadOnlyList<ToolbarItemViewModel> ActionPanelItems { get; }
    public IReadOnlyList<ToolbarItemViewModel> DrawingPanelItems { get; }
    public string SectionTitle => Section switch
    {
        SettingsSection.General => UiText.Get("Kadr.General"),
        SettingsSection.Editor => UiText.Get("Kadr.Editor"),
        SettingsSection.Shortcuts => UiText.Get("Kadr.Shortcuts"),
        SettingsSection.Toolbar => UiText.Get("Kadr.Toolbar"),
        _ => UiText.Get("Kadr.Recognition")
    };
    public IReadOnlyList<OcrLanguageViewModel> VisibleLanguages => Languages
        .Where(language => string.IsNullOrWhiteSpace(LanguageQuery) || language.Name.Contains(LanguageQuery.Trim(), StringComparison.OrdinalIgnoreCase)
            || language.Code.Contains(LanguageQuery.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(language => language.IsSelected).ThenBy(language => language.Name).ToArray();
    public IReadOnlyList<ShortcutBindingViewModel> VisibleShortcuts => Shortcuts
        .Where(shortcut => string.IsNullOrWhiteSpace(ShortcutQuery) || shortcut.Name.Contains(ShortcutQuery.Trim(), StringComparison.OrdinalIgnoreCase)
            || shortcut.DisplayName.Contains(ShortcutQuery.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();

    public event EventHandler? CloseRequested;

    private readonly SettingsStore _store;
    private readonly ISettingsApplication _application;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly ILocalizationService? _localization;

    [ObservableProperty] private int _backendIndex;
    [ObservableProperty] private int _outsideSelectionIndex;
    [ObservableProperty] private string? _localizationId;
    [ObservableProperty] private int _copyFormatIndex;
    [ObservableProperty] private int _modeIndex;
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private bool _autoSelectCreatedElements;
    [ObservableProperty] private bool _exitImmediately;
    [ObservableProperty] private bool _showCursorWhileDrawing;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralSection))]
    [NotifyPropertyChangedFor(nameof(IsEditorSection))]
    [NotifyPropertyChangedFor(nameof(IsShortcutsSection))]
    [NotifyPropertyChangedFor(nameof(IsRecognitionSection))]
    [NotifyPropertyChangedFor(nameof(IsToolbarSection))]
    [NotifyPropertyChangedFor(nameof(SectionTitle))]
    private SettingsSection _section;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(VisibleLanguages))] private string _languageQuery = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(VisibleShortcuts))] private string _shortcutQuery = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasErrorMessage))] private string? _errorMessage;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(SaveCommand))] private bool _isLoading;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(SaveCommand))] private bool _isRecordingShortcut;

    public SettingsViewModel(SettingsStore store, ISettingsApplication application,
        ILogger<SettingsViewModel> logger, bool canToggleCursor, ILocalizationService? localization = null)
    {
        _store = store;
        _application = application;
        _logger = logger;
        CanToggleCapturedCursor = canToggleCursor;
        _localization = localization;
        InterfaceLanguages = localization?.AvailableLocalizations ?? Array.Empty<LocalizationOption>();
        _localizationId = localization?.CurrentLocalization?.Id ?? store.Current.LocalizationId;
        AppSettings settings = store.Current;
        _backendIndex = (int)settings.Backend;
        _modeIndex = (int)settings.Mode;
        _autoStart = settings.AutoStart;
        _autoSelectCreatedElements = settings.AutoSelectCreatedElements;
        _exitImmediately = settings.ExitImmediately;
        _showCursorWhileDrawing = !settings.HideCursorWhileDrawing;
        _copyFormatIndex = (int)settings.CopyFormat;
        _outsideSelectionIndex = (int)settings.OutsideSelectionBehavior;
        ToolbarItems = ToolbarCatalog.Definitions.Where(definition => ToolbarCatalog.CanHide(definition.Id)).Select(definition => new ToolbarItemViewModel(definition)
        {
            IsVisible = !settings.HiddenToolbarItems.Contains(definition.Id)
        }).ToArray();
        ActionPanelItems = ToolbarItems.Where(item => ToolbarCatalog.IsActionPanel(item.Id)).ToArray();
        DrawingPanelItems = ToolbarItems.Where(item => !ToolbarCatalog.IsActionPanel(item.Id)).ToArray();
        Shortcuts = ShortcutCatalog.Definitions.Select(definition => new ShortcutBindingViewModel(definition,
            ShortcutCatalog.Get(settings, definition.Action), ShortcutCatalog.GetAlternative(settings, definition.Action))).ToArray();
        ExitShortcut.PropertyChanged += (sender, arguments) => OnPropertyChanged(nameof(ExitImmediatelyLabel));
        Languages = PaddleOcrService.SupportedLanguages.Select(code => new OcrLanguageViewModel(code,
            settings.OcrLanguages.Contains(code, StringComparer.Ordinal))).OrderBy(language => language.Name).ToArray();
    }

    public void ReportShortcutRecordingFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _logger.LogError(exception, "Shortcut recording could not be started");
        ErrorMessage = UiText.Get("Kadr.ShortcutRecordingFailed");
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(ExitImmediatelyLabel));
        foreach (LocalizedOptionViewModel option in CaptureModes.Concat(CopyFormats).Concat(OutsideSelectionModes))
        {
            option.RefreshLocalization();
        }
        foreach (ShortcutBindingViewModel shortcut in Shortcuts)
        {
            shortcut.RefreshLocalization();
        }
        foreach (OcrLanguageViewModel language in Languages)
        {
            language.RefreshLocalization();
        }
        foreach (ToolbarItemViewModel item in ToolbarItems)
        {
            item.RefreshLocalization();
        }
        OnPropertyChanged(nameof(VisibleShortcuts));
        OnPropertyChanged(nameof(VisibleLanguages));
    }

    [RelayCommand]
    private void SelectSection(SettingsSection section)
    {
        Section = section;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken ct)
    {
        if (IsLoading || IsRecordingShortcut)
        {
            return;
        }

        string[] languages = Languages.Where(language => language.IsSelected).Select(language => language.Code).ToArray();
        Dictionary<EditorAction, ShortcutGesture> shortcuts = Shortcuts.ToDictionary(row => row.Action, row => row.Gesture);
        AppSettings candidate = ShortcutCatalog.Apply(_store.Current, shortcuts) with { AlternativeShortcuts = Shortcuts.ToDictionary(row => row.Action, row => row.AlternativeGesture) };
        string? shortcutError = ShortcutPolicy.GetError(candidate);

        if (shortcutError is not null)
        {
            ErrorMessage = shortcutError;
            return;
        }

        if (languages.Length == 0)
        {
            ErrorMessage = UiText.Get("Kadr.SelectOcrLanguage");
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            await _application.ApplyAsync(candidate with
            {
                Backend = (CaptureBackend)BackendIndex,
                LocalizationId = LocalizationId,
                Mode = (CaptureMode)ModeIndex,
                AutoStart = AutoStart,
                AutoSelectCreatedElements = AutoSelectCreatedElements,
                ExitImmediately = ExitImmediately,
                HideCursorWhileDrawing = !ShowCursorWhileDrawing,
                OutsideSelectionBehavior = (OutsideSelectionBehavior)OutsideSelectionIndex,
                CopyFormat = (Services.Export.ScreenshotFormat)CopyFormatIndex,
                HiddenToolbarItems = ToolbarItems.Where(item => !item.IsVisible).Select(item => item.Id).ToArray(),
                OcrLanguages = languages
            }, ct);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = null;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Settings could not be applied");
            ErrorMessage = exception is System.ComponentModel.Win32Exception ? UiText.Get("Kadr.ShortcutConflict")
                : exception is PlatformNotSupportedException ? UiText.Get("Kadr.WgcUnavailable")
                : UiText.Get("Kadr.SettingsFailed");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanSave()
    {
        return !IsLoading && !IsRecordingShortcut;
    }

    partial void OnLocalizationIdChanged(string? value)
    {
        _ = ApplyLocalizationCommand.ExecuteAsync(value);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ApplyLocalizationAsync(string? id)
    {
        if (id is null || _localization is null)
        {
            return;
        }
        try
        {
            _localization.SelectSavedOrEnglishFallback(id);
            await _store.UpdateAsync(current => current with { LocalizationId = id }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Interface language could not be applied");
            ErrorMessage = UiText.Get("Kadr.SettingsFailed");
        }
    }
}
