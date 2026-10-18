using Kadr.Desktop.Services.Geometry;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class ToolbarPlacementTests
{
    [Theory]
    [InlineData(1280, 800)]
    [InlineData(800, 600)]
    public void PlacePair_FullScreen_GroupsBothPanelsAtBottomRight(int width, int height)
    {
        PixelRectangle viewport = new(6, 6, width - 12, height - 12);

        (PixelRectangle actions, PixelRectangle tools) = ToolbarPlacement.PlacePair(new PixelRectangle(0, 0, width, height),
            (54, 246), (624, 54), viewport, 10);

        Assert.Equal(viewport.Right, actions.Right);
        Assert.Equal(viewport.Bottom, actions.Bottom);
        Assert.Equal(viewport.Bottom, tools.Bottom);
        Assert.Equal(actions.X - 10, tools.Right);
        Assert.True(tools.X >= viewport.X);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(650)]
    public void PlacePair_FullWidthStrip_UsesFreeExteriorBand(double top)
    {
        PixelRectangle selection = new(0, top, 1280, 100);

        (PixelRectangle actions, PixelRectangle tools) = ToolbarPlacement.PlacePair(selection, (54, 246), (624, 54),
            new PixelRectangle(6, 6, 1268, 788), 10);

        Assert.True(actions.Intersect(selection).IsEmpty);
        Assert.True(tools.Intersect(selection).IsEmpty);
        Assert.True(actions.Intersect(tools).IsEmpty);
    }

    [Fact]
    public void PlacePair_BottomRightSpaceAvailable_UsesSameOutsideCorner()
    {
        PixelRectangle selection = new(300, 200, 500, 300);

        (PixelRectangle actions, PixelRectangle tools) = ToolbarPlacement.PlacePair(selection, (54, 246), (624, 54),
            new PixelRectangle(6, 6, 1268, 788), 10);

        Assert.Equal(810, actions.X);
        Assert.Equal(500, actions.Bottom);
        Assert.Equal(510, tools.Y);
        Assert.Equal(800, tools.Right);
    }

    [Fact]
    public void PlacePair_BottomRightAtScreenEdge_UsesOppositeOutsideCorner()
    {
        PixelRectangle selection = new(300, 200, 980, 600);

        (PixelRectangle actions, PixelRectangle tools) = ToolbarPlacement.PlacePair(selection, (54, 246), (624, 54),
            new PixelRectangle(6, 6, 1268, 788), 10);

        Assert.Equal(290, actions.Right);
        Assert.Equal(190, tools.Bottom);
        Assert.True(actions.Intersect(tools).IsEmpty);
        Assert.True(actions.Intersect(selection).IsEmpty);
        Assert.True(tools.Intersect(selection).IsEmpty);
    }

    [Fact]
    public void PlacePair_SmallSelectionAtBottomRight_UsesOppositeOutsideEdges()
    {
        PixelRectangle selection = new(1180, 720, 100, 80);

        (PixelRectangle actions, PixelRectangle tools) = ToolbarPlacement.PlacePair(selection, (54, 246), (624, 54),
            new PixelRectangle(6, 6, 1268, 788), 10);

        Assert.Equal(selection.X - 10, actions.Right);
        Assert.Equal(selection.Y - 10, tools.Bottom);
        Assert.True(actions.Intersect(tools).IsEmpty);
    }

    [Fact]
    public void PlacePair_SelectionsAcrossViewport_KeepsBothPanelsVisibleAndDisjoint()
    {
        PixelRectangle viewport = new(-1914, 6, 1908, 1068);

        foreach (int width in new int[] { 80, 400, 1500 })
        {
            foreach (int height in new int[] { 60, 320, 900 })
            {
                foreach (double x in new double[] { viewport.X, -width, -1000 })
                {
                    foreach (double y in new double[] { 0, 1080 - height, 100 })
                    {
                        PixelRectangle selection = new PixelRectangle(x, y, width, height).Intersect(new PixelRectangle(-1920, 0, 1920, 1080));
                        (PixelRectangle actions, PixelRectangle tools) = ToolbarPlacement.PlacePair(selection, (54, 246), (624, 54), viewport, 10);

                        Assert.Equal(actions, actions.Intersect(viewport));
                        Assert.Equal(tools, tools.Intersect(viewport));
                        Assert.True(actions.Intersect(tools).IsEmpty);
                    }
                }
            }
        }
    }
}
