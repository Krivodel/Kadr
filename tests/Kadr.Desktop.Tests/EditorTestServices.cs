using Kadr.Desktop.Services.Export;
using Kadr.Desktop.Services.Ocr;
using SkiaSharp;

namespace Kadr.Desktop.Tests;

internal sealed class EditorTestServices : IClipboardWriter, IFilePicker, IOcrService
{
    internal bool FailClipboard
    {
        get; set;
    }
    internal int ClipboardWrites
    {
        get; private set;
    }
    internal string OcrText { get; set; } = "";
    internal ScreenshotFormat? CopiedFormat
    {
        get; private set;
    }
    internal TaskCompletionSource<SaveDestination?>? SaveCompletion
    {
        get; set;
    }
    internal TaskCompletionSource<string>? OcrCompletion
    {
        get; set;
    }

    public Task WriteImageAsync(SKBitmap image, ScreenshotFormat format, CancellationToken ct)
    {
        if (FailClipboard)
        {
            throw new IOException("Clipboard unavailable in this test.");
        }

        ClipboardWrites++;
        CopiedFormat = format;
        return Task.CompletedTask;
    }

    public Task WriteTextAsync(string text, CancellationToken ct)
    {
        ClipboardWrites++;
        return Task.CompletedTask;
    }

    public Task<SaveDestination?> ChooseSaveDestinationAsync(string? directory, string format, CancellationToken ct)
    {
        return SaveCompletion?.Task ?? Task.FromResult<SaveDestination?>(null);
    }

    public Task<string> RecognizeAsync(SKBitmap image, IReadOnlyList<string> languages, CancellationToken ct)
    {
        return OcrCompletion?.Task ?? Task.FromResult(OcrText);
    }
}
