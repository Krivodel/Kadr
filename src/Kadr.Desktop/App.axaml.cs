using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia;
using Krivodeling.Localization.Avalonia;

using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Export;
using Kadr.Desktop.Services.Input;
using Kadr.Desktop.Services.Ocr;
using Kadr.Desktop.Services.Rendering;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.Services;
using Kadr.Desktop.ViewModels;

namespace Kadr.Desktop;

public sealed partial class App : Application
{
    private ServiceProvider? _services;
    private DesktopApplication? _host;

    public override void Initialize()
    {
        LocalizationText.InitializeIfNeeded(UiText.Catalog);
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            ServiceCollection services = new();
            services.AddLogging(builder => builder.SetMinimumLevel(FileLogger.MinimumLevel).AddProvider(new FileLoggerProvider()));
            services.AddSingleton<SettingsStore>();
            services.AddSingleton<ILocalizationService>(provider => new LocalizationService(
                new DirectoryLocalizationFileStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kadr", "Localizations")),
                UiText.Catalog, provider.GetRequiredService<ILogger<LocalizationService>>()));
            services.AddSingleton<GlobalHotkeyService>();
            services.AddSingleton<MonitorCaptureFactory>();
            services.AddSingleton<ICaptureService>(provider => new DesktopCaptureService(provider.GetRequiredService<MonitorCaptureFactory>(), provider.GetRequiredService<ILogger<DesktopCaptureService>>()));
            services.AddSingleton<AutoStartService>();
            services.AddSingleton<ISettingsApplication, SettingsApplication>();
            services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(5) });
            services.AddSingleton<OcrModelStore>();
            services.AddSingleton<IOcrService, TesseractOcrService>();
            services.AddSingleton(new DrawingToolRegistry());
            services.AddSingleton<IClipboardWriter, WindowsClipboardWriter>();
            services.AddSingleton<ImageFileWriter>();
            services.AddSingleton<IFilePicker>(provider => new AvaloniaFilePicker(() => _host?.EditorOwner));
            _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            ServiceProvider provider = _services;
            _host = new DesktopApplication(provider.GetRequiredService<ICaptureService>(), provider.GetRequiredService<GlobalHotkeyService>(),
                provider.GetRequiredService<SettingsStore>(),
                desktop => new EditorViewModel(new DrawingDocument(desktop, provider.GetRequiredService<SettingsStore>().Current.ShowCursor),
                    provider.GetRequiredService<DrawingToolRegistry>(), provider.GetRequiredService<IClipboardWriter>(),
                    provider.GetRequiredService<IFilePicker>(), provider.GetRequiredService<ImageFileWriter>(),
                    provider.GetRequiredService<IOcrService>(), provider.GetRequiredService<SettingsStore>(), provider.GetRequiredService<ILogger<EditorViewModel>>()),
                canToggle => new SettingsViewModel(provider.GetRequiredService<SettingsStore>(), provider.GetRequiredService<ISettingsApplication>(),
                    provider.GetRequiredService<OcrModelStore>(), provider.GetRequiredService<ILogger<SettingsViewModel>>(), canToggle,
                    provider.GetRequiredService<ILocalizationService>()),
                provider.GetRequiredService<ILogger<DesktopApplication>>(), lifetime, provider.GetRequiredService<ILocalizationService>());
            lifetime.Startup += OnStartup;
            lifetime.Exit += OnExit;
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync().ConfigureAwait(false);
        }

        if (_services is not null)
        {
            await _services.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async void OnStartup(object? sender, ControlledApplicationLifetimeStartupEventArgs arguments)
    {
        if (_host is not null)
        {
            if ((arguments.Args.Length > 1) && (arguments.Args[0] is "--editor-probe" or "--settings-probe")
                && (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime probeLifetime))
            {
                try
                {
                    if (arguments.Args[0] == "--settings-probe")
                    {
                        await _host.RunSettingsProbeAsync(arguments.Args[1], CancellationToken.None);
                    }
                    else
                    {
                        await _host.RunEditorProbeAsync(arguments.Args[1], CancellationToken.None);
                    }
                    probeLifetime.Shutdown();
                }
                catch (Exception exception)
                {
                    _services?.GetRequiredService<ILogger<App>>().LogError(exception, "Application UI probe failed");
                    probeLifetime.Shutdown(1);
                }
                return;
            }

            await _host.InitializeAsync(arguments.Args, CancellationToken.None);

            if (arguments.Args.Contains("--smoke-test", StringComparer.Ordinal)
                && (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime))
            {
                lifetime.Shutdown();
            }
        }
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs arguments)
    {
        _services?.GetRequiredService<ILogger<App>>().LogInformation("Kadr is exiting");
    }

    private void OnUnhandledException(object? sender, Avalonia.Threading.DispatcherUnhandledExceptionEventArgs arguments)
    {
        _services?.GetRequiredService<ILogger<App>>().LogCritical(arguments.Exception, "Unhandled UI exception");
    }

    private void OnUnobservedException(object? sender, UnobservedTaskExceptionEventArgs arguments)
    {
        _services?.GetRequiredService<ILogger<App>>().LogError(arguments.Exception, "Unobserved background task exception");
    }

    private void OnDomainException(object sender, UnhandledExceptionEventArgs arguments)
    {
        _services?.GetRequiredService<ILogger<App>>().LogCritical(arguments.ExceptionObject as Exception, "Unhandled process exception");
    }
}
