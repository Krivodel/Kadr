using SkiaSharp;

namespace Kadr.Desktop.Services.Ocr;

internal static class OcrImagePreparation
{
    private const int Border = 12;
    private const int TargetGlyphHeight = 28;
    private const int MaximumScale = 3;
    private const long MaximumPixels = 32_000_000;
    private const int LuminanceThreshold = 128;
    private const int BackgroundSampleDimension = 32;
    private const int AnalysisColumns = 2048;
    private const int MinimumRowInk = 2;
    private const int MinimumGlyphHeight = 4;
    private const int MaximumGlyphHeight = 96;

    internal static SKBitmap Prepare(SKBitmap image)
    {
        bool inverted = IsDarkBackground(image);
        int scale = GetScale(image, inverted);
        SKBitmap result = new((image.Width * scale) + (Border * 2), (image.Height * scale) + (Border * 2),
            SKColorType.Bgra8888, SKAlphaType.Premul);
        float sign = inverted ? -1 : 1;
        float offset = inverted ? 1 : 0;
        using SKColorFilter grayscale = SKColorFilter.CreateColorMatrix(new float[]
        {
            sign * 0.2126f, sign * 0.7152f, sign * 0.0722f, 0, offset,
            sign * 0.2126f, sign * 0.7152f, sign * 0.0722f, 0, offset,
            sign * 0.2126f, sign * 0.7152f, sign * 0.0722f, 0, offset,
            0, 0, 0, 1, 0
        });
        using SKPaint paint = new()
        {
            ColorFilter = grayscale
        };
        using SKCanvas canvas = new(result);
        canvas.Clear(SKColors.White);
        using SKImage source = SKImage.FromBitmap(image);
        canvas.DrawImage(source, new SKRect(Border, Border, Border + (image.Width * scale), Border + (image.Height * scale)),
            new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        return result;
    }

    private static bool IsDarkBackground(SKBitmap image)
    {
        int dark = 0;
        int total = 0;

        for (int y = 0; y < image.Height; y += Math.Max(1, image.Height / BackgroundSampleDimension))
        {
            for (int x = 0; x < image.Width; x += Math.Max(1, image.Width / BackgroundSampleDimension))
            {
                SKColor color = image.GetPixel(x, y);
                dark += (color.Alpha >= LuminanceThreshold) && (Luminance(color) < LuminanceThreshold) ? 1 : 0;
                total++;
            }
        }

        return dark > total / 2;
    }

    private static int GetScale(SKBitmap image, bool inverted)
    {
        List<int> bands = [];
        int height = 0;

        for (int y = 0; y <= image.Height; y++)
        {
            int ink = 0;
            int samples = 0;

            if (y < image.Height)
            {
                for (int x = 0; x < image.Width; x += Math.Max(1, image.Width / AnalysisColumns))
                {
                    SKColor color = image.GetPixel(x, y);
                    bool dark = Luminance(color) < LuminanceThreshold;
                    ink += (color.Alpha >= LuminanceThreshold) && (dark != inverted) ? 1 : 0;
                    samples++;
                }
            }

            if ((ink >= MinimumRowInk) && (ink < samples / 2))
            {
                height++;
            }
            else
            {
                if (height is >= MinimumGlyphHeight and <= MaximumGlyphHeight)
                {
                    bands.Add(height);
                }

                height = 0;
            }
        }

        bands.Sort();
        int glyphHeight = bands.Count == 0 ? TargetGlyphHeight / 2 : bands[bands.Count / 2];
        int scale = Math.Clamp((int)Math.Ceiling((double)TargetGlyphHeight / glyphHeight), 1, MaximumScale);

        while ((scale > 1) && ((long)image.Width * image.Height * scale * scale > MaximumPixels))
        {
            scale--;
        }

        return scale;
    }

    private static double Luminance(SKColor color)
    {
        return (color.Red * 0.2126) + (color.Green * 0.7152) + (color.Blue * 0.0722);
    }
}
