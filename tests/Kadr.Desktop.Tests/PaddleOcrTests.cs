using Kadr.Desktop.Services.Ocr;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Kadr.Desktop.Tests;

public sealed class PaddleOcrTests
{
    private readonly ITestOutputHelper _output;

    public PaddleOcrTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task RecognizeAsync_CyrillicChart_PreservesRowsAndSparseCharacters()
    {
        using SKBitmap image = SKBitmap.Decode(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cyrillic-chart.png"));
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-paddle-test-" + Guid.NewGuid().ToString("N"));
        PaddleModelStore models = new(directory);
        try
        {
            string recognized = await new PaddleOcrService(models).RecognizeAsync(image, new string[] { "rus", "eng" }, CancellationToken.None);
            _output.WriteLine(recognized);
            int after = OcrTestText.CountCharacterErrors(OcrTestText.CyrillicChart.ToUpperInvariant(), recognized.ToUpperInvariant());

            Assert.Equal(12, recognized.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.True(after <= 20, $"At most 20 of the 69 characters may be wrong; got {after}.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task RecognizeAsync_PreCancelledToken_DoesNotExtractModels()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-paddle-cancel-" + Guid.NewGuid().ToString("N"));
        using SKBitmap image = new(1, 1);
        PaddleOcrService service = new(new PaddleModelStore(directory));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RecognizeAsync(image,
            new string[] { "rus" }, new CancellationToken(true)));

        Assert.False(Directory.Exists(directory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecognizeAsync_ParagraphsOnLightAndDarkBackground_RecognizesContentWithoutSparseCharacterMode(bool dark)
    {
        using SKBitmap image = OcrTestImages.CreateScreenshot(16, dark);
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-paddle-paragraph-" + Guid.NewGuid().ToString("N"));
        PaddleOcrService service = new(new PaddleModelStore(directory));
        try
        {
            Assert.Empty(OcrLineSegmentation.FindSparseLines(image));

            string text = await service.RecognizeAsync(image, new string[] { "rus", "eng" }, CancellationToken.None);
            string normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            _output.WriteLine(normalized);

            Assert.Contains("Рисование неимоверно лагает", normalized, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("По нажатию", normalized, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("пунктирная рамка", normalized, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task EnsureAvailableAsync_CorruptCachedModel_RestoresBundledModelAndReusesValidFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-paddle-integrity-" + Guid.NewGuid().ToString("N"));
        PaddleModelStore models = new(directory);
        try
        {
            await models.EnsureAvailableAsync(CancellationToken.None);
            string recognizer = Path.Combine(models.DirectoryPath, "rec.onnx");
            string detector = Path.Combine(models.DirectoryPath, "det.onnx");
            DateTime detectorWritten = File.GetLastWriteTimeUtc(detector);
            byte[] expected = await File.ReadAllBytesAsync(recognizer);
            await File.WriteAllTextAsync(recognizer, "corrupt");

            await models.EnsureAvailableAsync(CancellationToken.None);

            Assert.Equal(expected, await File.ReadAllBytesAsync(recognizer));
            Assert.Equal(detectorWritten, File.GetLastWriteTimeUtc(detector));
            Assert.Empty(Directory.GetFiles(models.DirectoryPath, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
