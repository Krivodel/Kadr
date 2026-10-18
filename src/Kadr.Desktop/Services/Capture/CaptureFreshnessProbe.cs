using System.Text.Json;
using Kadr.Desktop.Services.Diagnostics;
using Kadr.Desktop.Services.Geometry;
using SkiaSharp;

namespace Kadr.Desktop.Services.Capture;

internal static class CaptureFreshnessProbe
{
    internal static Task<int> RunAsync(ICaptureService service, string path, CancellationToken ct)
    {
        return Task.Factory.StartNew(() => Run(service, path, ct), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static int Run(ICaptureService service, string path, CancellationToken ct)
    {
        DesktopMonitor monitor = NativeDesktop.GetMonitors()[0];
        int x = (int)monitor.Bounds.X + 30;
        int y = (int)monitor.Bounds.Y + 30;
        using NativeProbeWindow window = NativeProbeWindow.Create(new PixelRectangle(x, y, 64, 64), true);
        List<object> samples = [];
        int failures = 0;
        service.ConfigureAsync(CaptureBackend.DesktopDuplication, CaptureMode.OnDemand, ct).GetAwaiter().GetResult();
        using (CapturedDesktop warmup = service.CaptureAsync(ct).GetAwaiter().GetResult())
        {
        }

        SKColor[] colors = new SKColor[] { new(224, 32, 16), new(16, 224, 32), new(32, 16, 224), new(224, 224, 32) };

        foreach (SKColor expected in colors)
        {
            ct.ThrowIfCancellationRequested();
            window.Paint(expected);
            using CapturedDesktop desktop = service.CaptureAsync(ct).GetAwaiter().GetResult();
            SKColor actual = desktop.Image.GetPixel(x + 32 - (int)desktop.Bounds.X, y + 32 - (int)desktop.Bounds.Y);
            bool passed = (Math.Abs(actual.Red - expected.Red) < 8) && (Math.Abs(actual.Green - expected.Green) < 8)
                && (Math.Abs(actual.Blue - expected.Blue) < 8);
            failures += passed ? 0 : 1;
            samples.Add(new
            {
                Expected = expected.ToString(),
                Actual = actual.ToString(),
                Passed = passed,
                desktop.Metrics
            });
        }
        File.WriteAllText(path, JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }

}
