using Kadr.Desktop.Services.Ocr;
using SkiaSharp;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class OcrIntegrationTests
{
    [Fact]
    public async Task RecognizeAsync_BundledEnglishAndRussianModels_RecognizesBothLanguagesOffline()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-ocr-" + Guid.NewGuid().ToString("N"));
        using HttpClient http = new(new Services.Diagnostics.OfflineHttpHandler());
        OcrModelStore store = new(http, directory);
        TesseractOcrService service = new(store);
        using SKBitmap image = new(900, 180);
        using (SKCanvas canvas = new(image))
        using (SKPaint paint = new()
        {
            Color = SKColors.Black,
            IsAntialias = true
        })
        using (SKTypeface typeface = SKTypeface.FromFamilyName("Segoe UI"))
        using (SKFont font = new(typeface, 48))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawText("Kadr screenshot 123", 30, 65, font, paint);
            canvas.DrawText("Пример русского текста", 30, 135, font, paint);
        }

        try
        {
            Assert.True(store.IsInstalled("eng"));
            Assert.True(store.IsInstalled("rus"));
            Assert.False(Directory.Exists(directory));
            string text = await service.RecognizeAsync(image, new string[] { "eng", "rus" }, CancellationToken.None);

            Assert.Contains("Kadr", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Пример", text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                foreach (string file in Directory.GetFiles(directory))
                {
                    File.Delete(file);
                }

                Directory.Delete(directory);
            }
        }
    }

    [Fact]
    public void Verify_AlteredModel_RejectsContentsEvenWhenLengthMatches()
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes("changed");
        OcrLanguage model = new("test", "0000000000000000000000000000000000000000", bytes.Length);

        Assert.Throws<InvalidDataException>(() => OcrModelStore.Verify(bytes, model));
    }
}
