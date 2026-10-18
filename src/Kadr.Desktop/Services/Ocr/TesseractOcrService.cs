using SkiaSharp;
using Tesseract;

namespace Kadr.Desktop.Services.Ocr;

public sealed class TesseractOcrService : IOcrService
{
    private readonly OcrModelStore _models;

    public TesseractOcrService(OcrModelStore models)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
    }

    public async Task<string> RecognizeAsync(SKBitmap image, IReadOnlyList<string> languages, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(languages);

        foreach (string language in languages)
        {
            if (!File.Exists(Path.Combine(_models.DirectoryPath, language + ".traineddata")))
            {
                await _models.InstallAsync(language, ct).ConfigureAwait(false);
            }
        }

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            TesseractNativeRuntime.Initialize();
            using SKBitmap prepared = OcrImagePreparation.Prepare(image);
            using SKData png = prepared.Encode(SKEncodedImageFormat.Png, 100);
            using Pix pixels = Pix.LoadFromMemory(png.ToArray());
            using TesseractEngine engine = new(_models.DirectoryPath, string.Join('+', languages), EngineMode.LstmOnly);
            engine.SetVariable("user_defined_dpi", 300);
            using Page page = engine.Process(pixels, PageSegMode.Auto);
            string text = OcrTextLayout.Read(page);
            ct.ThrowIfCancellationRequested();
            return text;
        }, ct).ConfigureAwait(false);
    }
}
