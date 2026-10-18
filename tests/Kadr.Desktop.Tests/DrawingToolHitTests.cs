using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Rendering;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class DrawingToolHitTests
{
    [Fact]
    public void HitTest_EmptySpaceInsideLineBounds_DoesNotSelectLine()
    {
        DrawingElement line = TestDocuments.Element(DrawingToolId.Line, 10, 10, 100, 100);
        LineTool tool = new();

        bool hit = tool.HitTest(line, new PixelPoint(80, 20), 3);

        Assert.False(hit);
        Assert.True(tool.HitTest(line, new PixelPoint(51, 50), 3));
    }

    [Fact]
    public void HitTest_ArrowHead_SelectsBothWings()
    {
        DrawingElement arrow = TestDocuments.Element(DrawingToolId.Arrow, 10, 50, 110, 50) with
        {
            Thickness = 8
        };
        ArrowTool tool = new();

        bool upper = tool.HitTest(arrow, new PixelPoint(90, 38), 2);
        bool lower = tool.HitTest(arrow, new PixelPoint(90, 62), 2);

        Assert.True(upper);
        Assert.True(lower);
    }
}
