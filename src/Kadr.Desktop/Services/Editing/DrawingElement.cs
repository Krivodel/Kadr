using System.Collections.Immutable;

using Kadr.Desktop.Services.Geometry;

namespace Kadr.Desktop.Services.Editing;

public sealed record DrawingElement(Guid Id, DrawingToolId Tool, ImmutableList<PixelPoint> Points,
    uint Color, double Thickness, string Text = "", double? LabelSize = null, PixelPoint? Bend = null,
    double BlurStrength = DrawingDefaults.BlurStrength)
{
    public PixelRectangle Bounds
    {
        get
        {
            double left = Points.Min(point => point.X);
            double top = Points.Min(point => point.Y);
            double width = Math.Max(1, Points.Max(point => point.X) - left);
            double height = Math.Max(1, Points.Max(point => point.Y) - top);

            if ((Tool == DrawingToolId.Number) || (Tool == DrawingToolId.Text))
            {
                double size = LabelSize ?? (Tool == DrawingToolId.Number ? DrawingDefaults.GetNumberSize(Thickness) : DrawingDefaults.GetLabelSize(Thickness));
                width = Tool == DrawingToolId.Number ? size : Math.Max(size, Text.Split('\n').Max(line => line.Length) * size * 0.65);
                height = Tool == DrawingToolId.Number ? size : size * DrawingDefaults.TextLineSpacing * Math.Max(1, Text.Split('\n').Length);
            }

            return new PixelRectangle(left, top, width, height);
        }
    }

    public DrawingElement Translate(double x, double y)
    {
        if (x == 0 && y == 0)
        {
            return this;
        }
        return this with
        {
            Points = Points.Select(point => new PixelPoint(point.X + x, point.Y + y)).ToImmutableList(),
            Bend = Bend is PixelPoint bend ? new PixelPoint(bend.X + x, bend.Y + y) : null
        };
    }

    public DrawingElement Resize(PixelRectangle target)
    {
        PixelRectangle source = Bounds;
        double scaleX = target.Width / source.Width;
        double scaleY = target.Height / source.Height;
        double labelScale = Math.Abs(scaleX - 1) >= Math.Abs(scaleY - 1) ? scaleX : scaleY;
        return this with
        {
            Points = Points.Select(point => new PixelPoint(target.X + ((point.X - source.X) * scaleX),
                target.Y + ((point.Y - source.Y) * scaleY))).ToImmutableList(),
            Thickness = Tool is DrawingToolId.Text or DrawingToolId.Number
                ? Math.Clamp(Thickness * labelScale, DrawingDefaults.MinimumThickness, DrawingDefaults.MaximumThickness) : Thickness,
            LabelSize = Tool == DrawingToolId.Number ? source.Height * labelScale : LabelSize
        };
    }
}
