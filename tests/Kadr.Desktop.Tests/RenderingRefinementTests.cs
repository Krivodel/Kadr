using System.Collections.Immutable;

using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Rendering;
using SkiaSharp;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class RenderingRefinementTests
{
    [Theory]
    [InlineData(6)]
    [InlineData(18)]
    public void ArrowPreview_Thickness_UsesBodyDiameter(double thickness)
    {
        Assert.Equal(thickness * 2, ArrowTool.GetPreviewDiameter(thickness));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(18)]
    public void Export_ArrowAndLineWithEqualSize_ArrowBodyIsThicker(double thickness)
    {
        using DrawingDocument document = TestDocuments.Create(800, 400);
        document.SetSelection(document.Bounds);
        document.SetElements(ImmutableArray.Create(
            TestDocuments.Element(DrawingToolId.Line, 50, 100, 700, 100) with { Color = 0xffff0000, Thickness = thickness },
            TestDocuments.Element(DrawingToolId.Arrow, 50, 250, 700, 250) with { Color = 0xffff0000, Thickness = thickness }));
        using DocumentRenderer renderer = new(document, new DrawingToolRegistry());

        using SKBitmap image = renderer.Export();

        int lineWidth = Enumerable.Range(50, 100).Count(y => image.GetPixel(350, y).Green < 50);
        int arrowWidth = Enumerable.Range(200, 100).Count(y => image.GetPixel(350, y).Green < 50);
        Assert.True(arrowWidth > lineWidth, $"Arrow body {arrowWidth}px; line {lineWidth}px.");
    }

    [Fact]
    public void CreateScene_ChangingBlurInput_KeepsOldSnapshotAliveAfterCacheDisposal()
    {
        using DrawingDocument document = CreatePattern();
        DrawingElement line = TestDocuments.Element(DrawingToolId.Line, 40, 80, 200, 80);
        DrawingElement blur = TestDocuments.Element(DrawingToolId.Blur, 20, 20, 220, 140);
        document.SetElements(ImmutableArray.Create(line, blur));
        using DocumentRenderer renderer = new(document, new DrawingToolRegistry());
        using SKPicture before = renderer.CreateScene();
        using SKBitmap expected = new(document.Desktop.Image.Info);
        using (SKCanvas canvas = new(expected))
        {
            canvas.DrawPicture(before);
        }
        document.SetElements(ImmutableArray.Create(line.Translate(0, 20), blur));
        using SKPicture after = renderer.CreateScene();
        renderer.Dispose();
        using SKBitmap actual = new(document.Desktop.Image.Info);

        using (SKCanvas canvas = new(actual))
        {
            canvas.DrawPicture(before);
        }

        Assert.Equal(expected.Pixels, actual.Pixels);
    }

    [Fact]
    public void Export_MovingAndResizingBlur_PreservesIdenticalPixelsInSharedInterior()
    {
        using DrawingDocument document = CreatePattern();
        document.SetSelection(document.Bounds);
        DrawingElement blur = TestDocuments.Element(DrawingToolId.Blur, 20, 20, 200, 140);
        document.SetElements(ImmutableArray.Create(blur));
        using DocumentRenderer renderer = new(document, new DrawingToolRegistry());
        using SKBitmap first = renderer.Export();
        document.SetElements(ImmutableArray.Create(blur.Translate(40, 0)));
        using SKBitmap moved = renderer.Export();
        document.SetElements(ImmutableArray.Create(blur.Resize(new PixelRectangle(40, 10, 200, 140))));

        using SKBitmap resized = renderer.Export();

        for (int y = 45; y < 115; y++)
        {
            for (int x = 85; x < 175; x++)
            {
                Assert.Equal(first.GetPixel(x, y), moved.GetPixel(x, y));
                Assert.Equal(first.GetPixel(x, y), resized.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void Export_BlurOnColoredPattern_ReducesBroadImageDetail()
    {
        using DrawingDocument document = CreatePattern();
        document.SetSelection(document.Bounds);
        document.SetElements(ImmutableArray.Create(TestDocuments.Element(DrawingToolId.Blur, 0, 0, 256, 160) with { BlurStrength = 18 }));
        using DocumentRenderer renderer = new(document, new DrawingToolRegistry());

        using SKBitmap image = renderer.Export();

        int contrast = Math.Abs(image.GetPixel(88, 80).Red - image.GetPixel(104, 80).Red);
        Assert.True(contrast < 100, $"The filtered contrast was {contrast}; source contrast is 255.");
    }

    [Theory]
    [InlineData(DrawingToolId.Marker, 1.2, false)]
    [InlineData(DrawingToolId.Pencil, 12, true)]
    public void Export_FreehandStroke_KeepsShadowOnlyOnPencil(DrawingToolId tool, double thickness, bool shadow)
    {
        using DrawingDocument document = TestDocuments.Create(200, 120);
        document.SetSelection(document.Bounds);
        document.SetElements(ImmutableArray.Create(TestDocuments.Element(tool, 20, 50, 150, 50) with
        {
            Thickness = thickness
        }));
        using DocumentRenderer renderer = new(document, new DrawingToolRegistry());

        using SKBitmap image = renderer.Export();

        Assert.Equal(shadow, image.GetPixel(80, 59) != SKColors.White);
    }

    private static DrawingDocument CreatePattern()
    {
        SKBitmap image = new(256, 160, SKColorType.Bgra8888, SKAlphaType.Premul);

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                image.SetPixel(x, y, (x / 16) % 2 == 0 ? SKColors.Red : SKColors.Blue);
            }
        }

        DesktopMonitor monitor = new(1, new PixelRectangle(0, 0, image.Width, image.Height), "Pattern");
        return new DrawingDocument(new CapturedDesktop(image, new DesktopMonitor[] { monitor }, monitor.Bounds, null, true,
            new CaptureMetrics(0, 0, 0, Array.Empty<long>())), false);
    }
}
