using Kadr.Desktop.Services.Rendering;
using SkiaSharp;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class DrawingSourceTests
{
    [Fact]
    public void GetBlurredImage_RepeatedReadsAndDifferentStrength_CachesOnlyCurrentResult()
    {
        using SKPictureRecorder recorder = new();
        SKCanvas canvas = recorder.BeginRecording(new SKRect(0, 0, 1024, 768));
        canvas.Clear(SKColors.Blue);
        using SKPicture picture = recorder.EndRecording();
        using DrawingSource source = new(picture, new SKImageInfo(1024, 768));

        SKImage first = source.GetBlurredImage(12);
        SKImage unchanged = source.GetBlurredImage(12);
        SKImage changed = source.GetBlurredImage(40);

        Assert.Same(first, unchanged);
        Assert.NotSame(first, changed);
        using SKBitmap pixels = SKBitmap.FromImage(changed);
        Assert.Equal(SKColors.Blue, pixels.GetPixel(pixels.Width / 2, pixels.Height / 2));
    }
}
