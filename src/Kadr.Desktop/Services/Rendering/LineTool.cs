using SkiaSharp;

using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;

namespace Kadr.Desktop.Services.Rendering;

public sealed class LineTool : DrawingTool
{
    public override DrawingToolId Id => DrawingToolId.Line;
    public override string Name => UiText.Get("Kadr.Line");
    public override string Icon => "M4 20L20 4";

    public override bool HitTest(DrawingElement element, PixelPoint point, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(element);
        using SKPath path = CurveGeometry.CreatePath(element);
        return HitOutline(path, point, element.Thickness + tolerance * 2);
    }

    public override void Draw(SKCanvas canvas, DrawingElement element, int number, DrawingSource source)
    {
        using SKPaint paint = CreatePaint(element);
        using SKPath path = CurveGeometry.CreatePath(element);
        canvas.DrawPath(path, paint);
    }

}
