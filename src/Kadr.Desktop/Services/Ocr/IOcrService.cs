using SkiaSharp;

namespace Kadr.Desktop.Services.Ocr;

public interface IOcrService
{
    Task<string> RecognizeAsync(SKBitmap image, IReadOnlyList<string> languages, CancellationToken ct);
}
