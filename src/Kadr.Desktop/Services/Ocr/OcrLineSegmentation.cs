using SkiaSharp;

namespace Kadr.Desktop.Services.Ocr;

internal static class OcrLineSegmentation
{
    private const int MaximumAnalysisPixels = 8_000_000;
    private const double ComponentGapRatio = 0.25;
    private const double CharacterGapRatio = 0.35;
    private const double MaximumCharacterWidthRatio = 1.5;
    private const double MinimumSparseLineRatio = 0.8;

    internal static IReadOnlyList<SKRectI> FindSparseLines(SKBitmap image)
    {
        if ((long)image.Width * image.Height > MaximumAnalysisPixels)
        {
            return Array.Empty<SKRectI>();
        }

        bool inverted = OcrImagePreparation.IsDarkBackground(image);
        List<SKRectI> lines = [];
        int top = -1;
        int left = image.Width;
        int right = 0;
        for (int y = 0; y <= image.Height; y++)
        {
            int rowLeft = image.Width;
            int rowRight = 0;
            if (y < image.Height)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if (OcrImagePreparation.IsForeground(image.GetPixel(x, y), inverted))
                    {
                        rowLeft = Math.Min(rowLeft, x);
                        rowRight = x + 1;
                    }
                }
            }

            if (rowRight > rowLeft)
            {
                top = top < 0 ? y : top;
                left = Math.Min(left, rowLeft);
                right = Math.Max(right, rowRight);
            }
            else if (top >= 0)
            {
                lines.Add(new SKRectI(left, top, right, y));
                top = -1;
                left = image.Width;
                right = 0;
            }
        }

        if (lines.Count < 2 || lines.Count(line => IsSparse(image, line, inverted)) < lines.Count * MinimumSparseLineRatio)
        {
            return Array.Empty<SKRectI>();
        }

        return lines;
    }

    private static bool IsSparse(SKBitmap image, SKRectI line, bool inverted)
    {
        List<(int Left, int Right)> characters = [];
        int start = -1;
        for (int x = line.Left; x <= line.Right; x++)
        {
            bool ink = false;
            if (x < line.Right)
            {
                for (int y = line.Top; y < line.Bottom && !ink; y++)
                {
                    ink = OcrImagePreparation.IsForeground(image.GetPixel(x, y), inverted);
                }
            }

            if (ink && start < 0)
            {
                start = x;
            }
            else if (!ink && start >= 0)
            {
                if (characters.Count > 0 && start - characters[^1].Right <= line.Height * ComponentGapRatio)
                {
                    characters[^1] = (characters[^1].Left, x);
                }
                else
                {
                    characters.Add((start, x));
                }
                start = -1;
            }
        }

        return characters.Count >= 2
            && characters.All(character => character.Right - character.Left <= line.Height * MaximumCharacterWidthRatio)
            && characters.Zip(characters.Skip(1), (first, second) => second.Left - first.Right)
                .All(gap => gap >= line.Height * CharacterGapRatio);
    }
}
