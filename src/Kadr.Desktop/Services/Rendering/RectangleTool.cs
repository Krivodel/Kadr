using SkiaSharp;

using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;

namespace Kadr.Desktop.Services.Rendering;

public sealed class RectangleTool : DrawingTool
{
    public override DrawingToolId Id => DrawingToolId.Rectangle;
    public override bool HasSelectableInterior => true;
    public override string Name => UiText.Get("Kadr.Rectangle");
    public override string Icon => "M4 5H20V19H4Z";

    public override void Draw(SKCanvas canvas, DrawingElement element, int number, DrawingSource source)
    {
        using SKPaint paint = CreatePaint(element);
        canvas.DrawRect(GetRectangle(element), paint);
    }

    public override bool HitTest(DrawingElement element, PixelPoint point, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(element);
        PixelRectangle bounds = element.Bounds;
        double radius = element.Thickness / 2 + tolerance;
        if (bounds.Contains(point))
        {
            return Math.Min(Math.Min(point.X - bounds.X, bounds.Right - point.X),
                Math.Min(point.Y - bounds.Y, bounds.Bottom - point.Y)) <= radius;
        }
        return point.DistanceTo(new PixelPoint(Math.Clamp(point.X, bounds.X, bounds.Right), Math.Clamp(point.Y, bounds.Y, bounds.Bottom))) <= radius;
    }
}
