using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Xunit;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.ViewModels;
using Kadr.Desktop.Views;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Kadr.Desktop.Tests;

internal static class UiTestSession
{
    internal static async Task<SettingsStore> CreateSettingsAsync(AppSettings settings)
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-ui-settings-" + Guid.NewGuid().ToString("N"));
        SettingsStore store = new(NullLogger<SettingsStore>.Instance, Path.Combine(directory, "settings.json"));
        await store.SaveAsync(settings, CancellationToken.None);
        Directory.Delete(directory, true);
        return store;
    }

    internal static SettingsStore CreateSettingsStore()
    {
        return new SettingsStore(NullLogger<SettingsStore>.Instance,
            Path.Combine(Path.GetTempPath(), "Kadr-ui-settings-" + Guid.NewGuid().ToString("N"), "settings.json"));
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>().UseSkia().UseHarfBuzz()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, ShouldRenderOnUIThread = true });
    }
    internal static EditorWindow CreateEditorWindow(EditorViewModel model, SettingsStore? settings = null)
    {
        return new EditorWindow(model, model.Document.Desktop.Monitors[0], settings ?? CreateSettingsStore());
    }

    internal static SKBitmap CapturePixels(TopLevel window)
    {
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using MemoryStream stream = new();
        frame.Save(stream, PngBitmapEncoderOptions.Default);
        return SKBitmap.Decode(stream.ToArray());
    }

    internal static void SaveFrame(TopLevel window, string name)
    {
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
        Directory.CreateDirectory(directory);
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
    internal static async Task PumpFramesAsync(int milliseconds)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();

        do
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(8);
        }
        while (System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds < milliseconds);

        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    internal static async Task WaitForAsync(Func<bool> completed)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();

        while (!completed())
        {
            if (System.Diagnostics.Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(3))
            {
                throw new TimeoutException("The expected UI state was not reached within three seconds.");
            }

            await PumpFramesAsync(8);
        }
    }

    internal static async Task<IReadOnlyList<double>> ObserveTransitionAsync(AvaloniaObject control,
        StyledProperty<double> property, double target, Action change)
    {
        List<double> samples = [];
        EventHandler<AvaloniaPropertyChangedEventArgs> handler = (sender, arguments) =>
        {
            if (arguments.Property == property)
            {
                samples.Add(control.GetValue(property));
            }
        };
        control.PropertyChanged += handler;

        try
        {
            change();
            await WaitForAsync(() => control.GetValue(property) == target);
            return samples;
        }
        finally
        {
            control.PropertyChanged -= handler;
        }
    }

}
