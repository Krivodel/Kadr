using SkiaSharp;

namespace Kadr.Desktop.Services.Export;

public static class ImageEncoder
{
    public static byte[] Encode(SKBitmap image, ScreenshotFormat format)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (format == ScreenshotFormat.Png)
        {
            using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
            return png.ToArray();
        }

        if (format != ScreenshotFormat.Jpeg)
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        using SKBitmap flattened = new(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKCanvas canvas = new(flattened);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(image, 0, 0);
        using SKData jpeg = flattened.Encode(SKEncodedImageFormat.Jpeg, 95);
        return jpeg.ToArray();
    }
}
