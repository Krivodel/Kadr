using System.Collections.Immutable;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Rendering;
using SkiaSharp;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class ArrowRenderingTests
{
    [Fact]
    public void Export_ThreeArrowThicknesses_RendersFilledTaperedBodyAndHead()
    {
        using DrawingDocument document = TestDocuments.Create(440, 460, new SKColor(29, 30, 46));
        DrawingElement first = TestDocuments.Element(DrawingToolId.Arrow, 60, 55, 373, 55) with
        {
            Thickness = 10
        };
        DrawingElement second = TestDocuments.Element(DrawingToolId.Arrow, 60, 165, 359, 165) with
        {
            Thickness = 5
        };
        DrawingElement third = TestDocuments.Element(DrawingToolId.Arrow, 52, 337, 410, 305) with
        {
            Thickness = 24
        };
        document.SetElements(ImmutableArray.Create(first, second, third));
        document.SetSelection(document.Bounds);
        using DocumentRenderer renderer = new(document, new DrawingToolRegistry());

        using SKBitmap image = renderer.Export();

        Assert.True(image.GetPixel(330, 55).Red > 200);
        Assert.True(image.GetPixel(346, 43).Red > 200);
        Assert.True(image.GetPixel(300, 50).Red > 200);
        Assert.True(image.GetPixel(75, 47).Red < 100);
        Assert.True(new ArrowTool().HitTest(first, new Services.Geometry.PixelPoint(346, 43), 2));
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(directory, "arrow-reference-v2.png"), png.ToArray());
    }
}
