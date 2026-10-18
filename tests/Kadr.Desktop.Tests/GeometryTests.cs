using Kadr.Desktop.Services.Geometry;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class GeometryTests
{
    [Fact]
    public void SnapPointerToBoundary_LastScreenPixel_SelectsFullPhysicalResolution()
    {
        PixelRectangle screen = new(-2560, -1440, 2560, 1440);
        PixelPoint end = SelectionGeometry.SnapPointerToBoundary(new PixelPoint(-1, -1), screen);

        PixelRectangle selected = PixelRectangle.FromPoints(new PixelPoint(screen.X, screen.Y), end);

        Assert.Equal(screen, selected);
    }

    [Theory]
    [InlineData(0, 0, ResizeHandle.Left | ResizeHandle.Top)]
    [InlineData(120, 60, ResizeHandle.Right)]
    [InlineData(60, 130, ResizeHandle.Bottom)]
    [InlineData(60, 60, ResizeHandle.None)]
    public void HitSelectionEdge_OutsideAndInsidePoints_ReturnsExpectedResizeDirection(double x, double y, ResizeHandle expected)
    {
        ResizeHandle result = SelectionGeometry.HitSelectionEdge(new PixelRectangle(20, 20, 80, 80), new PixelPoint(x, y), 5);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Place_WideToolbarAnchoredAtSelectionRight_ExtendsLeft()
    {
        PixelRectangle selection = new(900, 300, 200, 150);
        const int Width = 700;

        (PixelRectangle _, PixelRectangle result) = ToolbarPlacement.PlacePair(selection, (54, 246), (Width, 54),
            new PixelRectangle(0, 0, 1920, 1080), 10);

        Assert.Equal(selection.Right, result.Right);
        Assert.Equal(400, result.X);
    }

    [Fact]
    public void Snap_ReversedDragAndFractionalBounds_PreservesPhysicalPixelRectangle()
    {
        PixelRectangle rectangle = PixelRectangle.FromPoints(new PixelPoint(100.3, 80.8), new PixelPoint(-10, -20));

        PixelRectangle result = rectangle.Snap(new PixelRectangle(0, 0, 200, 100));

        Assert.Equal(new PixelRectangle(0, 0, 100, 81), result);
    }

    [Fact]
    public void Resize_CrossingOppositeCorner_NormalizesBounds()
    {
        PixelRectangle source = new(10, 10, 20, 20);

        PixelRectangle result = SelectionGeometry.Resize(source, ResizeHandle.Left | ResizeHandle.Top, new PixelPoint(50, 60));

        Assert.Equal(new PixelRectangle(30, 30, 20, 30), result);
    }

    [Theory]
    [InlineData(0, 255u, 0u, 0u)]
    [InlineData(120, 0u, 255u, 0u)]
    [InlineData(240, 0u, 0u, 255u)]
    public void FromHsv_PrimaryHue_ReturnsExpectedChannels(double hue, uint red, uint green, uint blue)
    {
        uint color = ColorMath.FromHsv(hue, 1, 1, 255);

        Assert.Equal(0xff000000u | (red << 16) | (green << 8) | blue, color);
    }

    [Fact]
    public void Place_SelectionAtBottomRight_KeepsToolbarWithinNegativeOriginMonitor()
    {
        PixelRectangle monitor = new(-1920, 0, 1920, 1080);

        (PixelRectangle actions, PixelRectangle result) = ToolbarPlacement.PlacePair(new PixelRectangle(-100, 1000, 100, 80),
            (54, 246), (700, 54), monitor, 8);

        Assert.True(result.X >= monitor.X);
        Assert.Equal(938, result.Y);
        Assert.True(actions.Intersect(result).IsEmpty);
        Assert.True(result.Right <= monitor.Right);
        Assert.True(result.Bottom <= monitor.Bottom);
    }
}
