using SkiaSharp;

namespace Kadr.Desktop.Services.Ocr;

internal static class OcrImagePreparation
{
    private const int LuminanceThreshold = 128;
    private const int BackgroundSampleDimension = 32;

    internal static bool IsDarkBackground(SKBitmap image)
    {
        return HasDarkBackground(GetBackgroundHistogram(image));
    }

    internal static bool IsForeground(SKColor color, bool inverted)
    {
        return color.Alpha >= LuminanceThreshold && (Luminance(color) < LuminanceThreshold) != inverted;
    }

    private static int[] GetBackgroundHistogram(SKBitmap image)
    {
        int[] histogram = new int[byte.MaxValue + 1];

        for (int y = 0; y < image.Height; y += Math.Max(1, image.Height / BackgroundSampleDimension))
        {
            for (int x = 0; x < image.Width; x += Math.Max(1, image.Width / BackgroundSampleDimension))
            {
                SKColor color = image.GetPixel(x, y);
                int value = color.Alpha >= LuminanceThreshold ? (int)Luminance(color) : byte.MaxValue;
                histogram[value]++;
            }
        }

        return histogram;
    }

    private static bool HasDarkBackground(IReadOnlyList<int> histogram)
    {
        return histogram.Take(LuminanceThreshold).Sum() > histogram.Sum() / 2;
    }

    private static double Luminance(SKColor color)
    {
        return (color.Red * 0.2126) + (color.Green * 0.7152) + (color.Blue * 0.0722);
    }

}
