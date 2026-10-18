using System.Diagnostics;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using SkiaSharp;
using Tesseract;

using Kadr.Desktop.Services.Ocr;

namespace Kadr.Desktop.Services.Diagnostics;

internal static class DistributionProbe
{
    internal static async Task<int> RunAsync(string outputPath, CancellationToken ct)
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-distribution-" + Guid.NewGuid().ToString("N"));
        using FileLoggerProvider logging = new();
        ILogger logger = logging.CreateLogger(nameof(DistributionProbe));
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            using HttpClient http = new(new OfflineHttpHandler());
            OcrModelStore models = new(http, directory);
            bool availableOffline = models.IsInstalled("eng") && models.IsInstalled("rus");
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
            TesseractOcrService service = new(models);
            string text = await service.RecognizeAsync(image, new string[] { "eng", "rus" }, ct).ConfigureAwait(false);
            bool english = text.Contains("Kadr", StringComparison.OrdinalIgnoreCase);
            bool russian = text.Contains("Пример", StringComparison.OrdinalIgnoreCase);
            foreach (string code in new string[] { "eng", "rus" })
            {
                byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(directory, code + ".traineddata"), ct).ConfigureAwait(false);
                OcrModelStore.Verify(bytes, models.Manifest.Languages.Single(language => language.Code == code));
            }
            using Process process = Process.GetCurrentProcess();
            string[] nativeNames = new string[] { "tesseract50.dll", "leptonica-1.82.0.dll", "libSkiaSharp.dll", "vcruntime140.dll", "msvcp140.dll" };
            object[] nativeModules = process.Modules.Cast<ProcessModule>()
                .Where(module => nativeNames.Contains(module.ModuleName, StringComparer.OrdinalIgnoreCase))
                .Select(module => (object)new { module.ModuleName, module.FileName }).ToArray();
            bool passed = availableOffline && english && russian;
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(new
            {
                Passed = passed, ModelsAvailableOffline = availableOffline, EnglishRecognized = english, RussianRecognized = russian,
                ModelChecksumsVerified = true, ElapsedMs = watch.Elapsed.TotalMilliseconds,
                Executable = Environment.ProcessPath, AppContext.BaseDirectory, NativeDirectory = TesseractEnviornment.CustomSearchPath,
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
