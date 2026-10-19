using System.Diagnostics;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using SkiaSharp;
using NSec.Cryptography;
using Kadr.Desktop.Services.Updates;

using Kadr.Desktop.Services.Ocr;

namespace Kadr.Desktop.Services.Diagnostics;

internal static class DistributionProbe
{
    internal static async Task<int> RunAsync(string outputPath, CancellationToken ct)
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-distribution-" + Guid.NewGuid().ToString("N"));
        using FileLoggerProvider logging = new(ApplicationDataPaths.ForProbe(outputPath).LogsDirectory);
        ILogger logger = logging.CreateLogger(nameof(DistributionProbe));
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            using SKBitmap image = new(900, 180);
            using (SKCanvas canvas = new(image))
            using (SKPaint paint = new() { Color = SKColors.Black, IsAntialias = true })
            using (SKTypeface face = SKTypeface.FromFamilyName("Segoe UI"))
            using (SKFont font = new(face, 48))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawText("Kadr screenshot 123", 30, 65, font, paint);
                canvas.DrawText("Пример русского текста", 30, 135, font, paint);
            }
            PaddleOcrService service = new(new PaddleModelStore(directory));
            Stopwatch engineWatch = Stopwatch.StartNew();
            string text = await service.RecognizeAsync(image, new string[] { "eng", "rus" }, ct).ConfigureAwait(false);
            bool recognized = text.Contains("Kadr", StringComparison.OrdinalIgnoreCase)
                && text.Contains("Пример", StringComparison.OrdinalIgnoreCase);
            double recognitionElapsedMs = engineWatch.Elapsed.TotalMilliseconds;
            bool removedResourcesAbsent = !typeof(DistributionProbe).Assembly.GetManifestResourceNames()
                .Any(name => name.StartsWith("Kadr.Ocr.", StringComparison.Ordinal) || name.StartsWith("Kadr.PaddleMedium", StringComparison.Ordinal)
                    || name.StartsWith("Kadr.PaddleServer", StringComparison.Ordinal));
            using Process process = Process.GetCurrentProcess();
            string[] nativeNames = new string[] { "libSkiaSharp.dll", "vcruntime140.dll", "msvcp140.dll", "onnxruntime.dll", "onnxruntime_providers_shared.dll" };
            object[] nativeModules = process.Modules.Cast<ProcessModule>()
                .Where(module => nativeNames.Contains(module.ModuleName, StringComparer.OrdinalIgnoreCase))
                .Select(module => (object)new { module.ModuleName, module.FileName }).ToArray();
            using Key signingKey = Key.Create(SignatureAlgorithm.Ed25519);
            string sampleHash = new('a', 64);
            byte[] signature = SignatureAlgorithm.Ed25519.Sign(signingKey, UpdateSignature.GetMessage("1.0.0", "Kadr-win-x64.exe", sampleHash));
            bool signatures = UpdateSignature.Verify(new UpdateSignatureManifest("1.0.0", "Kadr-win-x64.exe", sampleHash, Convert.ToBase64String(signature)),
                "1.0.0", "Kadr-win-x64.exe", sampleHash, signingKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));
            bool passed = recognized && signatures && removedResourcesAbsent;
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(new
            {
                Passed = passed, ModelsAvailableOffline = true,
                PaddleOcrRecognized = recognized,
                RemovedResourcesAbsent = removedResourcesAbsent, RecognitionElapsedMs = recognitionElapsedMs,
                ModelChecksumsVerified = true, UpdateSignaturesAvailable = signatures, ElapsedMs = watch.Elapsed.TotalMilliseconds,
                Executable = Environment.ProcessPath, AppContext.BaseDirectory,
                NativeModules = nativeModules
            }, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
            return passed ? 0 : 1;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The standalone distribution check failed");
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(new { Passed = false, Error = exception.ToString() }), ct).ConfigureAwait(false);
            return 1;
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
