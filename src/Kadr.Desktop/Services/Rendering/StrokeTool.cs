using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using SkiaSharp;

namespace Kadr.Desktop.Services.Rendering;

public abstract class StrokeTool : DrawingTool
{
    protected abstract byte Opacity
    {
        get;
    }

    public override bool HitTest(DrawingElement element, PixelPoint point, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(element);

        PixelPoint previous = element.Points[0];
        double radius = DrawingDefaults.GetStrokeThickness(element.Tool, element.Thickness) / 2;

        foreach (PixelPoint current in element.Points)
        {
            if (point.DistanceToSegment(previous, current) <= tolerance + radius)
            {
                return true;
            }

            previous = current;
        }

        return false;
    }

    public override void Draw(SKCanvas canvas, DrawingElement element, int number, DrawingSource source)
    {
        using SKPaint paint = CreatePaint(element);
        paint.Color = paint.Color.WithAlpha((byte)(paint.Color.Alpha * Opacity / 255));
        using SKPath path = new();
        PixelPoint first = element.Points[0];
        path.MoveTo((float)first.X, (float)first.Y);

        foreach (PixelPoint point in element.Points.Skip(1))
        {
            path.LineTo((float)point.X, (float)point.Y);
        }

        if (element.Points.All(point => point == first))
        {
            paint.Style = SKPaintStyle.Fill;
            canvas.DrawCircle((float)first.X, (float)first.Y, paint.StrokeWidth / 2, paint);
        }
        else
        {
            canvas.DrawPath(path, paint);
        }
    }
}
