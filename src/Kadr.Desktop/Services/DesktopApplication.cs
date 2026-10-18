using System.Diagnostics;
using System.ComponentModel;

using Microsoft.Extensions.Logging;

using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia;
using Krivodeling.Localization.Avalonia;

using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Input;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.Services.Updates;
using Kadr.Desktop.Services.Errors;
using Kadr.Desktop.ViewModels;
using Kadr.Desktop.Views;

namespace Kadr.Desktop.Services;

internal sealed class DesktopApplication : IAsyncDisposable
{
    internal TopLevel? EditorOwner => _editors.FirstOrDefault(window => window.IsActive) ?? _editors.FirstOrDefault();

    private readonly ICaptureService _capture;
    private readonly GlobalHotkeyService _hotkey;
    private readonly SettingsStore _settings;
    private readonly Func<CapturedDesktop, EditorViewModel> _createEditor;
    private readonly Func<bool, SettingsViewModel> _createSettings;
    private readonly ILogger<DesktopApplication> _logger;
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly ILocalizationService _localization;
    private readonly List<EditorWindow> _editors = [];
    private readonly TrayIcon _tray;
    private readonly CancellationTokenSource _stop = new();
    private EditorViewModel? _editor;
    private SettingsWindow? _settingsWindow;
    private int _capturing;
    private readonly UpdateViewModel _updates;
    private readonly UpdateSessionStore _sessions;
    private readonly IUpdateService _updateService;
    private Task? _updateChecks;
    private bool _restarting;

    internal DesktopApplication(ICaptureService capture, GlobalHotkeyService hotkey, SettingsStore settings,
        Func<CapturedDesktop, EditorViewModel> createEditor, Func<bool, SettingsViewModel> createSettings,
        ILogger<DesktopApplication> logger, IClassicDesktopStyleApplicationLifetime lifetime, ILocalizationService localization,
        IUpdateService updates, UpdateSessionStore sessions, IViewModelErrorHandler updateErrors)
    {
        _capture = capture;
        _hotkey = hotkey;
        _settings = settings;
        _createEditor = createEditor;
        _createSettings = createSettings;
        _logger = logger;
        _lifetime = lifetime;
        _localization = localization;
        _sessions = sessions;
        _updateService = updates;
        _updates = new UpdateViewModel(updates, SaveUpdateSessionAsync, RestartAfterUpdate, updateErrors, _sessions.Complete);
        _updates.PropertyChanged += OnUpdateChanged;
        NativeMenu menu = new();
        NativeMenuItem captureItem = new(UiText.Get("Kadr.Capture"));
        NativeMenuItem settingsItem = new(UiText.Get("Kadr.Settings"));
        NativeMenuItem exitItem = new(UiText.Get("Kadr.Exit"));
        LocalizationBinding.Bind(captureItem, NativeMenuItem.HeaderProperty, "Kadr.Capture");
        LocalizationBinding.Bind(settingsItem, NativeMenuItem.HeaderProperty, "Kadr.Settings");
        LocalizationBinding.Bind(exitItem, NativeMenuItem.HeaderProperty, "Kadr.Exit");
        captureItem.Click += OnCaptureRequested;
        settingsItem.Click += OnSettingsRequested;
        exitItem.Click += OnExitRequested;
        menu.Items.Add(captureItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);
        using Stream stream = AssetLoader.Open(new Uri("avares://Kadr/Assets/AppIcon.ico"));
        _tray = new TrayIcon { Icon = new WindowIcon(stream), Menu = menu, IsVisible = true };
        _tray.Clicked += OnSettingsRequested;
        _hotkey.Activated += OnCaptureRequested;
        _settings.Changed += OnSettingsChanged;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _updates.PropertyChanged -= OnUpdateChanged;
        _updates.Dispose();
        if (_updateChecks is not null)
        {
            try
            {
                await _updateChecks.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        _tray.Dispose();
        _hotkey.Activated -= OnCaptureRequested;
        _settings.Changed -= OnSettingsChanged;
        _stop.Dispose();
    }

    internal async Task InitializeAsync(string[] args, CancellationToken ct)
    {
        bool firstRun = !_settings.Exists;
        await _settings.LoadAsync(ct);
        await _localization.RefreshAvailableLocalizationsAsync(ct);
        if (_settings.Current.LocalizationId is string savedLocalization)
        {
            _localization.SelectSavedOrEnglishFallback(savedLocalization);
        }
        else
        {
            _localization.ReconcileCurrentOrSystemDefault();
        }
        await _capture.ConfigureAsync(_settings.Current.Backend, _settings.Current.Mode, ct);

        try
        {
            await _hotkey.SetAsync(ShortcutCatalog.GetBindings(_settings.Current, EditorAction.Capture), ct);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The screenshot shortcut could not be registered");
            ShowSettings(UiText.Get("Kadr.CaptureShortcutConflict"));
        }

        string? shortcutError = ShortcutPolicy.GetError(_settings.Current);
        if ((shortcutError is not null) && (_settingsWindow is null))
        {
            ShowSettings(shortcutError);
        }

        if (!args.Contains("--silent", StringComparer.Ordinal) && !args.Contains("--smoke-test", StringComparer.Ordinal)
            && (firstRun || args.Contains("--settings", StringComparer.Ordinal)))
        {
            ShowSettings(null);
        }
        if (!args.Contains("--smoke-test", StringComparer.Ordinal))
        {
            await RestoreUpdateSessionAsync(args, ct);
            Progress<UpdateRelease?> releases = new(_updates.SetRelease);
            _updateChecks = Task.Run(() => CheckUpdatesAsync(releases, _stop.Token));
        }
    }

    private async Task CheckUpdatesAsync(IProgress<UpdateRelease?> progress, CancellationToken ct)
    {
        using PeriodicTimer timer = new(TimeSpan.FromHours(6));
        do
        {
            try
            {
                progress.Report(await _updateService.CheckAsync(ct).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "The automatic update check failed");
            }
        }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }

    private async Task<string> SaveUpdateSessionAsync(CancellationToken ct)
    {
        EditorViewModel editor = _editor ?? throw new InvalidOperationException("The screenshot editor is closed.");
        foreach (EditorWindow window in _editors)
        {
            window.PrepareForUpdate();
        }
        await editor.RememberPreferencesAsync(ct);
        EditorSessionSnapshot snapshot = EditorSessionSnapshot.Capture(editor);
        return await Task.Run(() => _sessions.SaveAsync(snapshot, ct), ct);
    }

    private void RestartAfterUpdate()
    {
        _restarting = true;
        _lifetime.Shutdown();
    }

    private void OnUpdateChanged(object? sender, PropertyChangedEventArgs arguments)
    {
        if (arguments.PropertyName == nameof(UpdateViewModel.IsLoading) && _editor is not null)
        {
            _editor.IsLoading = _updates.IsLoading;
        }
    }

    private async Task RestoreUpdateSessionAsync(string[] args, CancellationToken ct)
    {
        try
        {
            int index = Array.IndexOf(args, "--resume-update");
            string? id = index >= 0 && index + 1 < args.Length ? args[index + 1] : await _sessions.GetPendingAsync(ct);
            if (id is null)
            {
                return;
            }
            if (Interlocked.CompareExchange(ref _capturing, 1, 0) != 0)
            {
                return;
            }
            EditorSessionSnapshot snapshot = await Task.Run(() => _sessions.LoadAsync(id, ct), ct);
            CapturedDesktop desktop = await Task.Run(snapshot.CreateDesktop, ct);
            OpenEditor(desktop, snapshot);
            Interlocked.Exchange(ref _capturing, 1);
            _sessions.Complete(id);
        }
        catch (Exception exception)
        {
            if (_editor is null)
            {
                Interlocked.Exchange(ref _capturing, 0);
            }
            _logger.LogError(exception, "The screenshot session could not be restored after updating");
            ShowSettings(UiText.Get("Kadr.UpdateRestoreFailed"));
        }
    }

    private void OpenEditor(CapturedDesktop desktop, EditorSessionSnapshot? snapshot = null)
    {
        _editor = _createEditor(desktop);
        _editor.Updates = _updates;
        snapshot?.Restore(_editor);
        _editor.CloseRequested += OnEditorFinished;
        _editor.SettingsRequested += OnEditorSettings;
        foreach (DesktopMonitor monitor in desktop.Monitors)
        {
            EditorWindow window = new(_editor, monitor, _settings);
            _editors.Add(window);
            window.Closing += OnEditorClosing;
            window.Show();
        }
        _editors.FirstOrDefault(window => NativeDesktop.IsPointerOnMonitor(window.Monitor))?.Activate();
    }

    internal Task RunEditorProbeAsync(string path, CancellationToken ct)
    {
        return Diagnostics.EditorPerformanceProbe.RunAsync(_createEditor, _settings, path, ct);
    }

    private async Task ActivateAsync()
    {
        if (_updates.IsLoading)
        {
            return;
        }
        if (Interlocked.CompareExchange(ref _capturing, 1, 0) != 0)
        {
            return;
        }

        try
        {
            using IDisposable? scope = _logger.BeginScope("Screenshot");
            CapturedDesktop desktop = await _capture.CaptureAsync(_stop.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                OpenEditor(desktop);
                _logger.LogInformation("Editor windows created after {ElapsedMs} ms; capture readback {ReadbackMs} ms",
                    Stopwatch.GetElapsedTime(desktop.Metrics.RequestedAt).TotalMilliseconds, desktop.Metrics.ReadbackMilliseconds);
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The screenshot editor could not be opened");
            await Dispatcher.UIThread.InvokeAsync(() => ShowSettings(UiText.Get("Kadr.CaptureFailed")));
            Interlocked.Exchange(ref _capturing, 0);
        }
    }

    private void ShowSettings(string? error)
    {
        if (_settingsWindow is null)
        {
            SettingsViewModel model = _createSettings(_editor?.Document.Desktop.CanToggleCursor ?? true);
            model.ErrorMessage = error;
            _settingsWindow = new SettingsWindow { DataContext = model, Topmost = _editor is not null };
            model.CloseRequested += (sender, arguments) => { _settingsWindow?.Close(); };
            _settingsWindow.Closed += (sender, arguments) => { _settingsWindow = null; };
            _settingsWindow.Show();
        }
        else if ((_settingsWindow.DataContext is SettingsViewModel model) && (error is not null))
        {
            model.ErrorMessage = error;
        }

        _settingsWindow.Activate();
    }

    private async Task CloseEditorAsync()
    {
        EditorViewModel? editor = _editor;
        _editor = null;

        if (editor is not null)
        {
            editor.CloseRequested -= OnEditorFinished;
            editor.SettingsRequested -= OnEditorSettings;

            EditorWindow[] windows = _editors.ToArray();
            await Task.WhenAll(windows.Select(window => window.FadeOutAsync()));

            foreach (EditorWindow window in windows)
            {
                window.Closing -= OnEditorClosing;
                window.Close();
            }

            _editors.Clear();
            await editor.RememberPreferencesAsync(_stop.Token);
            editor.Dispose();
            Interlocked.Exchange(ref _capturing, 0);
        }
    }

    private void OnCaptureRequested(object? sender, EventArgs arguments)
    {
        if (_settingsWindow?.IsRecordingShortcut != true)
        {
            _ = ActivateAsync();
        }
    }

    private void OnSettingsRequested(object? sender, EventArgs arguments)
    {
        ShowSettings(null);
    }

    internal async Task RunSettingsProbeAsync(string path, CancellationToken ct)
    {
        _tray.IsVisible = false;
        await _settings.LoadAsync(ct);
        await _localization.RefreshAvailableLocalizationsAsync(ct);
        _localization.ReconcileCurrentOrSystemDefault();
        await Diagnostics.SettingsWindowProbe.RunAsync(_createSettings(true), path, ct);
    }

    private void OnSettingsChanged(object? sender, EventArgs arguments)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyEditorSettings();
        }
        else
        {
            Dispatcher.UIThread.Post(ApplyEditorSettings, DispatcherPriority.Send);
        }
    }

    private void ApplyEditorSettings()
    {
        _editor?.ApplySettings(_settings.Current);
        _updates.RefreshLocalization();
    }

    private void OnExitRequested(object? sender, EventArgs arguments)
    {
        _ = ExitAsync();
    }

    private async Task ExitAsync()
    {
        _updates.Dispose();
        if (_updates.ApplyCommand.ExecutionTask is Task applying)
        {
            await applying;
        }
        _restarting = true;
        _lifetime.Shutdown();
    }

    private void OnEditorFinished(object? sender, EventArgs arguments)
    {
        _ = CloseEditorAsync();
    }

    private void OnEditorSettings(object? sender, EventArgs arguments)
    {
        if (_editor is not null)
        {
            _editor.IsPreview = false;
        }

        ShowSettings(null);
    }

    private void OnEditorClosing(object? sender, WindowClosingEventArgs arguments)
    {
        if (_restarting)
        {
            return;
        }
        arguments.Cancel = true;

        if (_editor is { IsLoading: false })
        {
            _ = CloseEditorAsync();
        }
    }
}
