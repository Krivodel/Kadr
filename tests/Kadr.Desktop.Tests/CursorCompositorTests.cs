using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Geometry;
using SkiaSharp;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class CursorCompositorTests
{
    [Fact]
    public void Draw_MonochromeAndXorMasks_PreservesBlackWhiteTransparentAndInvertedPixels()
    {
        using SKBitmap image = new(4, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
        image.Erase(new SKColor(40, 80, 120));
        CapturedCursor cursor = new(CursorShapeKind.Monochrome, new PixelPoint(-1920, 0), 4, 1, 1,
            new byte[] { 0b00110000, 0b01010000 }, 0);

        CursorCompositor.Draw(image, cursor, -1920, 0);

        Assert.Equal(SKColors.Black, image.GetPixel(0, 0));
        Assert.Equal(SKColors.White, image.GetPixel(1, 0));
        Assert.Equal(new SKColor(40, 80, 120), image.GetPixel(2, 0));
        Assert.Equal(new SKColor(215, 175, 135), image.GetPixel(3, 0));
    }

    [Fact]
    public void Draw_MaskedColorCursor_UsesReplaceAndXorWithoutChangingSource()
    {
        using SKBitmap image = new(2, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
        image.Erase(new SKColor(32, 64, 128));
        byte[] source = new byte[] { 255, 0, 0, 0, 255, 255, 255, 255 };
        CapturedCursor cursor = new(CursorShapeKind.MaskedColor, default, 2, 1, 8, source, 0);

        CursorCompositor.Draw(image, cursor, 0, 0);

        Assert.Equal(SKColors.Blue, image.GetPixel(0, 0));
        Assert.Equal(new SKColor(223, 191, 127), image.GetPixel(1, 0));
        Assert.Equal((byte)255, source[7]);
    }
}
