using System.Collections.Immutable;

using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Rendering;
using SkiaSharp;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class MarkerRenderingTests
{
    [Theory]
    [InlineData(1, 10, 240)]
    [InlineData(6, 60, 240)]
    [InlineData(128, 1280, 240)]
    [InlineData(1, 10, 80)]
    [InlineData(6, 60, 80)]
    [InlineData(128, 1280, 80)]
    public void Export_MarkerStrokeAndDot_UseTenfoldWidthInPreviewAndResult(double thickness, int expectedWidth, int endX)
    {
        using DrawingDocument document = TestDocuments.Create(320, 1400);
        document.SetSelection(document.Bounds);
        DrawingElement marker = TestDocuments.Element(DrawingToolId.Marker, 80, 700, endX, 700) with
        {
            Thickness = thickness,
            Color = 0xffff0000
        };
        document.SetElements(ImmutableArray.Create(marker));
        using DocumentRenderer renderer = new(document, new DrawingToolRegistry());

        using SKBitmap result = renderer.Export();
        using SKImage preview = renderer.CreatePreview();
        using SKBitmap previewPixels = SKBitmap.FromImage(preview);

        int centerX = (80 + endX) / 2;
        int actualWidth = Enumerable.Range(0, result.Height).Count(y => result.GetPixel(centerX, y).Green < 220);
        Assert.Equal(expectedWidth, actualWidth);
        Assert.Equal(previewPixels.Pixels, result.Pixels);
        Assert.Equal(thickness, Assert.Single(document.Elements).Thickness);
    }

    [Theory]
    [InlineData(160, 119, true)]
    [InlineData(160, 121, false)]
    [InlineData(61, 100, true)]
    [InlineData(59, 100, false)]
    public void HitTest_MarkerOuterEdge_MatchesVisibleStroke(double x, double y, bool expected)
    {
        DrawingElement marker = TestDocuments.Element(DrawingToolId.Marker, 80, 100, 240, 100) with { Thickness = 4 };
        MarkerTool tool = new();

        bool hit = tool.HitTest(marker, new PixelPoint(x, y), 0);

        Assert.Equal(expected, hit);
    }
}
