using SkiaSharp;

namespace Kadr.Desktop.Services.Export;

public interface IClipboardWriter
{
    Task WriteImageAsync(SKBitmap image, ScreenshotFormat format, CancellationToken ct);
    Task WriteTextAsync(string text, CancellationToken ct);
}
