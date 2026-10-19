using SkiaSharp;

namespace Kadr.Desktop.Services.Ocr;

internal static class OcrLineReader
{
    internal static string Recognize(SKBitmap image, IReadOnlyList<SKRectI> lines,
        Func<SKBitmap, CancellationToken, string> recognize, CancellationToken ct)
    {
        List<string> text = [];

        foreach (SKRectI line in lines)
        {
            ct.ThrowIfCancellationRequested();
            using SKBitmap crop = new();

            if (!image.ExtractSubset(crop, line))
            {
                throw new InvalidDataException("The OCR line could not be extracted.");
            }

            text.Add(recognize(crop, ct));
        }

        return string.Join(Environment.NewLine, text).Trim();
    }
}
