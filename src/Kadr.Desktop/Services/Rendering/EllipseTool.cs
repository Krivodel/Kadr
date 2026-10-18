using SkiaSharp;

using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;

namespace Kadr.Desktop.Services.Rendering;

public sealed class EllipseTool : DrawingTool
{
    public override DrawingToolId Id => DrawingToolId.Ellipse;
    public override bool HasSelectableInterior => true;
    public override string Name => UiText.Get("Kadr.Ellipse");
    public override string Icon => "M21 12A9 7 0 1 1 3 12A9 7 0 1 1 21 12";

    public override void Draw(SKCanvas canvas, DrawingElement element, int number, DrawingSource source)
    {
        using SKPaint paint = CreatePaint(element);
        canvas.DrawOval(GetRectangle(element), paint);
    }

    public override bool HitTest(DrawingElement element, PixelPoint point, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(element);
        using SKPath path = new();
        path.AddOval(GetRectangle(element));
        return HitOutline(path, point, element.Thickness + tolerance * 2);
    }

    public override bool HitInterior(DrawingElement element, PixelPoint point, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(element);
        using SKPath path = new();
        path.AddOval(GetRectangle(element));
        return path.Contains((float)point.X, (float)point.Y);
    }
}
