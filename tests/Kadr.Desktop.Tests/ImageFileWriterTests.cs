using Kadr.Desktop.Services.Export;
using SkiaSharp;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class ImageFileWriterTests
{
    [Theory]
    [InlineData("png", true)]
    [InlineData("jpeg", false)]
    public async Task SaveAsync_TransparentImage_UsesFormatSpecificBackground(string format, bool transparency)
    {
        string path = Path.Combine(Path.GetTempPath(), "Kadr-test-" + Guid.NewGuid().ToString("N") + "." + format);
        using SKBitmap image = new(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul);
        image.Erase(SKColors.Transparent);
        ImageFileWriter writer = new();

        try
        {
            await writer.SaveAsync(image, new SaveDestination(path, format), CancellationToken.None);
            using SKBitmap saved = SKBitmap.Decode(path);

            Assert.Equal(16, saved.Width);
            Assert.Equal(transparency ? 0 : 255, saved.GetPixel(5, 5).Alpha);

            if (!transparency)
            {
                Assert.Equal(SKColors.White, saved.GetPixel(5, 5));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
