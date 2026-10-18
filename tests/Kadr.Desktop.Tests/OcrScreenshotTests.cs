using Kadr.Desktop.Services.Ocr;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Kadr.Desktop.Tests;

public sealed class OcrScreenshotTests
{
    private static readonly string[] Paragraphs = new string[]
    {
        "1. Рисование неимоверно лагает, любое перемещение, любое изменение размера, вообще всё рисование лагучее до ужаса.",
        "2. Режим \"По нажатию\" не работает, скринит просто чёрный экран и курсор.",
        "3. Если выделить весь экран, то не захватывается по 1 пикселю с каждой стороны (получается например не 2560x1440, а 2559x1439).",
        "4. В прямоугольнике выделения скриншота не должно быть этих квадратиков с каждой стороны и с каждого угла, должна быть только пунктирная рамка."
    };

    private readonly ITestOutputHelper _output;

    public OcrScreenshotTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(14, false)]
    [InlineData(16, true)]
    public async Task RecognizeAsync_SmallWrappedNumberedRussianParagraphs_PreservesNumbersAndReflowsParagraphs(int fontSize, bool dark)
    {
        using SKBitmap image = CreateScreenshot(fontSize, dark);
        using HttpClient http = new();
        OcrModelStore models = new(http, Path.Combine(AppContext.BaseDirectory, "tessdata"));
        TesseractOcrService service = new(models);

        string text = await service.RecognizeAsync(image, new string[] { "rus", "eng" }, CancellationToken.None);

        _output.WriteLine(text);
        using SKBitmap prepared = OcrImagePreparation.Prepare(image);
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
        using SKData diagnostic = prepared.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(directory, $"ocr-fixture-{fontSize}-{dark}.png"), diagnostic.ToArray());
        string[] lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);

        for (int index = 0; index < lines.Length; index++)
        {
            Assert.StartsWith((index + 1) + ".", lines[index]);
        }

        Assert.Contains("до ужаса.", lines[0]);
        Assert.Contains("пунктирная рамка", lines[3]);
    }

    private static SKBitmap CreateScreenshot(int fontSize, bool dark)
    {
        const int Width = 820;
        SKBitmap image = new(Width, 300);
        using SKCanvas canvas = new(image);
        using SKTypeface face = SKTypeface.FromFamilyName("Segoe UI");
        using SKFont font = new(face, fontSize);
        using SKPaint paint = new()
        {
            Color = dark ? SKColors.White : SKColors.Black,
            IsAntialias = true
        };
        canvas.Clear(dark ? new SKColor(26, 27, 30) : SKColors.White);
        float y = 24;

        foreach (string paragraph in Paragraphs)
        {
            string line = string.Empty;

            foreach (string word in paragraph.Split(' '))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;

                if (font.MeasureText(candidate) > Width - 40)
                {
                    canvas.DrawText(line, 15, y, font, paint);
                    y += fontSize + 6;
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }

            canvas.DrawText(line, 15, y, font, paint);
            y += (fontSize + 6) * 2;
        }

        return image;
    }
}
