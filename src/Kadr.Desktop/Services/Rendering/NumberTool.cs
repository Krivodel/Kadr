using SkiaSharp;

using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;

namespace Kadr.Desktop.Services.Rendering;

public sealed class NumberTool : DrawingTool
{
    public override DrawingToolId Id => DrawingToolId.Number;
    public override string Name => UiText.Get("Kadr.NumberTool");
    public override string Icon => "M21 12A9 9 0 1 1 3 12A9 9 0 1 1 21 12 M10 9L12 7V17 M9 17H15";

    public override void Draw(SKCanvas canvas, DrawingElement element, int number, DrawingSource source)
    {
        SKRect rectangle = GetRectangle(element);
        using SKPaint paint = CreatePaint(element);
        paint.Style = SKPaintStyle.Fill;
        paint.Color = SKColors.White;
        canvas.DrawOval(rectangle, paint);
        paint.ImageFilter = null;
        paint.Color = new SKColor(element.Color);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = (float)Math.Min(Math.Max(element.Thickness, 1), rectangle.Width / 8);
        canvas.DrawOval(rectangle, paint);
        paint.Style = SKPaintStyle.Fill;
        using SKTypeface typeface = SKTypeface.FromFamilyName(DrawingDefaults.TypefaceName, SKFontStyle.Bold);
        using SKFont font = new(typeface, rectangle.Height * 0.58f);
        string text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        float width = font.MeasureText(text);
        canvas.DrawText(text, rectangle.MidX - (width / 2), rectangle.MidY - ((font.Metrics.Ascent + font.Metrics.Descent) / 2), font, paint);
    }

    public override bool HitTest(DrawingElement element, PixelPoint point, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(element);
        PixelRectangle bounds = element.Bounds;
        double stroke = Math.Min(Math.Max(element.Thickness, 1), bounds.Width / 8);
        PixelPoint center = new(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        return point.DistanceTo(center) <= bounds.Width / 2 + stroke / 2 + tolerance;
    }
}
