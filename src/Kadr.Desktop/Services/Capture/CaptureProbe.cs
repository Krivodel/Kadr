using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Kadr.Desktop.Services.Capture;

internal static class CaptureProbe
{
    internal static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        string outputPath = args.Length > 1 ? args[1] : "capture-probe.png";
        CaptureBackend backend = args.Contains("--wgc", StringComparer.Ordinal)
            ? CaptureBackend.WindowsGraphicsCapture : CaptureBackend.DesktopDuplication;
        CaptureMode mode = args.Contains("--continuous", StringComparer.Ordinal) ? CaptureMode.Continuous : CaptureMode.OnDemand;
        string? iterationArgument = args.FirstOrDefault(argument => argument.StartsWith("--iterations=", StringComparison.Ordinal));
        int count = iterationArgument is null ? 1 : int.Parse(iterationArgument.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture);

        if ((count < 1) || (count > 1000))
        {
            throw new ArgumentOutOfRangeException(nameof(args), "Probe iterations must be between 1 and 1000.");
        }

        await using DesktopCaptureService service = new(new MonitorCaptureFactory(), NullLogger<DesktopCaptureService>.Instance);

        if (args.Contains("--verify-freshness", StringComparer.Ordinal))
        {
            return await CaptureFreshnessProbe.RunAsync(service, outputPath, ct).ConfigureAwait(false);
        }

        if (args.Contains("--exercise-lifecycle", StringComparer.Ordinal))
        {
            return await ExerciseLifecycleAsync(service, outputPath, count, ct).ConfigureAwait(false);
        }

        await service.ConfigureAsync(backend, mode, ct).ConfigureAwait(false);
        List<CaptureMetrics> samples = [];

        if ((mode == CaptureMode.Continuous) || (backend == CaptureBackend.DesktopDuplication))
        {
            using CapturedDesktop warmup = await service.CaptureAsync(ct).ConfigureAwait(false);
        }

        using System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess();
        TimeSpan cpuBefore = process.TotalProcessorTime;
        long idleStart = System.Diagnostics.Stopwatch.GetTimestamp();

        await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

        double idleCpuPercent = (process.TotalProcessorTime - cpuBefore).TotalSeconds
            / Math.Max(0.001, System.Diagnostics.Stopwatch.GetElapsedTime(idleStart).TotalSeconds) / Environment.ProcessorCount * 100;

        for (int index = 1; index < count; index++)
        {
            using CapturedDesktop sample = await service.CaptureAsync(ct).ConfigureAwait(false);
            samples.Add(sample.Metrics);
        }

        using CapturedDesktop desktop = await service.CaptureAsync(ct).ConfigureAwait(false);
        samples.Add(desktop.Metrics);
        using SKData image = desktop.Image.Encode(SKEncodedImageFormat.Png, 100);
        await File.WriteAllBytesAsync(outputPath, image.ToArray(), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(outputPath + ".json", System.Text.Json.JsonSerializer.Serialize(new
        {
            desktop.Bounds,
            Monitors = desktop.Monitors.Select(monitor => new { monitor.Name, monitor.Bounds }),
            desktop.CanToggleCursor,
            Cursor = desktop.Cursor is null ? null : new
            {
                desktop.Cursor.Kind,
                desktop.Cursor.Width,
                desktop.Cursor.Height
            },
            desktop.Metrics,
            Samples = samples,
            IdleCpuPercent = idleCpuPercent,
            WorkingSetBytes = process.WorkingSet64,
            Backend = backend.ToString(),
            Mode = mode.ToString()
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> ExerciseLifecycleAsync(DesktopCaptureService service, string outputPath, int count, CancellationToken ct)
    {
        List<object> results = [];
        int failures = 0;

        for (int index = 0; index < count; index++)
        {
            foreach (CaptureBackend backend in Enum.GetValues<CaptureBackend>())
            {
                foreach (CaptureMode mode in Enum.GetValues<CaptureMode>())
                {
                    try
                    {
                        await service.ConfigureAsync(backend, mode, ct).ConfigureAwait(false);
                        using CapturedDesktop desktop = await service.CaptureAsync(ct).ConfigureAwait(false);
                        results.Add(new
                        {
                            Index = index,
                            Backend = backend.ToString(),
                            Mode = mode.ToString(),
                            desktop.Bounds,
                            desktop.Metrics,
                            HasImagePixels = HasImagePixels(desktop.Image)
                        });
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        failures++;
                        results.Add(new
                        {
                            Index = index,
                            Backend = backend.ToString(),
                            Mode = mode.ToString(),
                            Error = exception.GetType().Name,
                            exception.HResult
                        });
                    }
                }
            }
        }

        await File.WriteAllTextAsync(outputPath, System.Text.Json.JsonSerializer.Serialize(results,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        return failures == 0 ? 0 : 1;
    }

    private static bool HasImagePixels(SKBitmap image)
    {
        for (int y = 0; y < image.Height; y += Math.Max(1, image.Height / 64))
        {
            for (int x = 0; x < image.Width; x += Math.Max(1, image.Width / 64))
            {
                SKColor pixel = image.GetPixel(x, y);

                if ((pixel.Red > 8) || (pixel.Green > 8) || (pixel.Blue > 8))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
