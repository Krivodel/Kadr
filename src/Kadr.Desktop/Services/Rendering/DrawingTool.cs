using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using SkiaSharp;

namespace Kadr.Desktop.Services.Rendering;

public abstract class DrawingTool
{
    public abstract DrawingToolId Id
    {
        get;
    }
    public abstract string Name
    {
        get;
    }
    public abstract string Icon
    {
        get;
    }
    public virtual bool StartsGroup => false;
    public virtual bool HasBlurredIcon => false;
    public virtual bool UsesSource => false;
    public virtual bool HasSelectableInterior => false;
    public virtual bool HasShadow => true;

    public abstract void Draw(SKCanvas canvas, DrawingElement element, int number, DrawingSource source);

    public virtual bool HitTest(DrawingElement element, PixelPoint point, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(element);
        PixelRectangle bounds = element.Bounds;
        return new PixelRectangle(bounds.X - tolerance, bounds.Y - tolerance,
            bounds.Width + (tolerance * 2), bounds.Height + (tolerance * 2)).Contains(point);
    }

    public virtual bool HitInterior(DrawingElement element, PixelPoint point, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(element);
        return HasSelectableInterior && element.Bounds.Contains(point);
    }

    protected static bool HitOutline(SKPath path, PixelPoint point, double width)
    {
        using SKPaint paint = new() { Style = SKPaintStyle.Stroke, StrokeWidth = (float)width, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using SKPath outline = new();
        paint.GetFillPath(path, outline);
        return outline.Contains((float)point.X, (float)point.Y);
    }

    protected static SKRect GetRectangle(DrawingElement element)
    {
        return new SKRect((float)element.Bounds.X, (float)element.Bounds.Y,
            (float)element.Bounds.Right, (float)element.Bounds.Bottom);
    }

    protected SKPaint CreatePaint(DrawingElement element)
    {
        using SKImageFilter? shadow = HasShadow ? SKImageFilter.CreateDropShadow(0, (float)DrawingShadow.OffsetY,
            (float)DrawingShadow.Sigma, (float)DrawingShadow.Sigma, new SKColor(0, 0, 0, DrawingShadow.Opacity)) : null;
        return new SKPaint
        {
            Color = new SKColor(element.Color),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)DrawingDefaults.GetStrokeThickness(element.Tool, element.Thickness),
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            ImageFilter = shadow
        };
    }
}
