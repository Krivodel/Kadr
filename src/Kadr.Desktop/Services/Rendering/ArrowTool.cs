using SkiaSharp;

using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;

namespace Kadr.Desktop.Services.Rendering;

public sealed class ArrowTool : DrawingTool
{
    private const double CurveSampleSpacing = 4;
    private const double ThicknessMultiplier = 2;
    private const int MaximumCurveSamples = 2048;
    public override DrawingToolId Id => DrawingToolId.Arrow;
    public override string Name => UiText.Get("Kadr.Arrow");
    public override string Icon => "M4 20L20 4 M9 4H20V15";

    public override void Draw(SKCanvas canvas, DrawingElement element, int number, DrawingSource source)
    {
        DrawArrow(canvas, element);
    }

    public static double GetPreviewDiameter(double thickness)
    {
        return thickness * ThicknessMultiplier;
    }

    public override bool HitTest(DrawingElement element, PixelPoint point, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(element);
        using SKPath path = CreatePath(element);

        if (path.Contains((float)point.X, (float)point.Y))
        {
            return true;
        }

        using SKPaint stroke = new()
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)(tolerance * 2)
        };
        using SKPath outline = new();
        stroke.GetFillPath(path, outline);
        return outline.Contains((float)point.X, (float)point.Y);
    }

    private void DrawArrow(SKCanvas canvas, DrawingElement element)
    {
        using SKPath path = CreatePath(element);
        using SKPaint paint = CreatePaint(element);
        using SKPathEffect rounding = SKPathEffect.CreateCorner((float)Math.Max(0.6, element.Thickness * 0.65));
        paint.Style = SKPaintStyle.Fill;
        paint.PathEffect = rounding;
        canvas.DrawPath(path, paint);
    }

    private static SKPath CreatePath(DrawingElement element)
    {
        PixelPoint start = element.Points[0];
        PixelPoint end = element.Points[^1];
        using SKPath centerline = CurveGeometry.CreatePath(element);
        using SKPathMeasure measure = new(centerline, false);
        double length = Math.Max(0.01, measure.Length);
        double chordLength = Math.Max(0.01, start.DistanceTo(end));
        double alongX = (end.X - start.X) / chordLength;
        double alongY = (end.Y - start.Y) / chordLength;
        double thickness = Math.Min(element.Thickness * ThicknessMultiplier, length / 3);
        double headLength = Math.Min(length * 0.55, Math.Max(8, thickness * 3.5));
        double headWidth = headLength * 0.6;
        double neck = Math.Min(thickness * 0.7, headWidth * 0.7);
        double tail = thickness * 0.3;
        SKPoint Local(double distance, double side)
        {
            if (element.Bend is not null && measure.GetPositionAndTangent((float)Math.Clamp(distance, 0, length), out SKPoint point, out SKPoint tangent))
            {
                double extension = distance < 0 ? distance : 0;
                return new SKPoint((float)(point.X + tangent.X * extension - tangent.Y * side),
                    (float)(point.Y + tangent.Y * extension + tangent.X * side));
            }
            return new SKPoint((float)(start.X + (alongX * distance) - (alongY * side)),
                (float)(start.Y + (alongY * distance) + (alongX * side)));
        }

        SKPath path = new();
        double bodyLength = length - headLength;
        int samples = element.Bend is null ? 1 : Math.Clamp((int)Math.Ceiling(bodyLength / CurveSampleSpacing), 8, MaximumCurveSamples);
        path.MoveTo(Local(0, -tail));
        for (int index = 1; index <= samples; index++)
        {
            double progress = (double)index / samples;
            path.LineTo(Local(bodyLength * progress, -(tail + (neck - tail) * progress)));
        }
        path.LineTo(Local(length - headLength - (thickness * 0.5), -headWidth));
        path.LineTo(Local(length, 0));
        path.LineTo(Local(length - headLength - (thickness * 0.5), headWidth));
        path.LineTo(Local(length - headLength, neck));
        for (int index = samples - 1; index >= 0; index--)
        {
            double progress = (double)index / samples;
            path.LineTo(Local(bodyLength * progress, tail + (neck - tail) * progress));
        }
        path.CubicTo(Local(-tail * 1.33, tail), Local(-tail * 1.33, -tail), Local(0, -tail));
        path.Close();
        return path;
    }
}
