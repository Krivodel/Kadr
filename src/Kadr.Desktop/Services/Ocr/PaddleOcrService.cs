using RapidOcrNet;
using SkiaSharp;

namespace Kadr.Desktop.Services.Ocr;

public sealed class PaddleOcrService : IOcrService
{
    public static IReadOnlyList<string> SupportedLanguages { get; } = Array.AsReadOnly(new string[] { "rus", "eng", "ukr", "bel" });

    private readonly PaddleModelStore _models;

    public PaddleOcrService(PaddleModelStore models)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
    }

    public static bool SupportsLanguage(string language)
    {
        return SupportedLanguages.Contains(language, StringComparer.Ordinal);
    }

    public async Task<string> RecognizeAsync(SKBitmap image, IReadOnlyList<string> languages, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(languages);
        if (languages.Count == 0 || languages.Any(language => !SupportsLanguage(language)))
        {
            throw new ArgumentException("The requested languages are not supported by PaddleOCR.", nameof(languages));
        }

        await _models.EnsureAvailableAsync(ct).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            using SKBitmap prepared = new(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (SKCanvas canvas = new(prepared))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(image, 0, 0);
            }

            IReadOnlyList<SKRectI> lines = OcrLineSegmentation.FindSparseLines(prepared);

            if (lines.Count > 0)
            {
                using TextRecognizer recognizer = new();
                recognizer.InitModel(Path.Combine(_models.DirectoryPath, "rec.onnx"), Path.Combine(_models.DirectoryPath, "keys.txt"), 2);
                return OcrLineReader.Recognize(prepared, lines, (crop, token) =>
                {
                    TextLine recognizedLine = recognizer.GetTextLine(crop, token);
                    return string.Concat(recognizedLine.Chars ?? Array.Empty<string>()).Trim();
                }, ct);
            }

            using RapidOcr engine = new();
            engine.InitModels(Path.Combine(_models.DirectoryPath, "det.onnx"), Path.Combine(_models.DirectoryPath, "cls.onnx"),
                Path.Combine(_models.DirectoryPath, "rec.onnx"), Path.Combine(_models.DirectoryPath, "keys.txt"), 2);
            RapidOcrOptions options = GetOptions(image);
            OcrResult result = engine.Detect(prepared, options, ct);
            return result.StrRes.Trim();
        }, ct).ConfigureAwait(false);
    }

    internal static RapidOcrOptions GetOptions(SKBitmap image)
    {
        return RapidOcrOptions.Default with { ImgResize = Math.Clamp(Math.Max(image.Width, image.Height), 1024, 4096) };
    }
}
