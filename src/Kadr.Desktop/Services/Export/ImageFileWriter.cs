using SkiaSharp;

namespace Kadr.Desktop.Services.Export;

public sealed class ImageFileWriter
{
    public async Task SaveAsync(SKBitmap image, SaveDestination destination, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(destination);
        bool jpeg = string.Equals(destination.Format, "jpeg", StringComparison.OrdinalIgnoreCase);
        byte[] bytes = await Task.Run(() => ImageEncoder.Encode(image, jpeg ? ScreenshotFormat.Jpeg : ScreenshotFormat.Png), ct).ConfigureAwait(false);
        string temporary = destination.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, ct).ConfigureAwait(false);
            File.Move(temporary, destination.Path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

}
