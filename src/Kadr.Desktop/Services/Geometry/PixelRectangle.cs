namespace Kadr.Desktop.Services.Geometry;

public readonly record struct PixelRectangle(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool IsEmpty => (Width <= 0) || (Height <= 0);

    public static PixelRectangle FromPoints(PixelPoint start, PixelPoint end)
    {
        return new PixelRectangle(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
            Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
    }

    public bool Contains(PixelPoint point)
    {
        return (point.X >= X) && (point.X <= Right) && (point.Y >= Y) && (point.Y <= Bottom);
    }

    public PixelRectangle Intersect(PixelRectangle other)
    {
        double left = Math.Max(X, other.X);
        double top = Math.Max(Y, other.Y);
        return new PixelRectangle(left, top, Math.Max(0, Math.Min(Right, other.Right) - left),
            Math.Max(0, Math.Min(Bottom, other.Bottom) - top));
    }

    public PixelRectangle Translate(double x, double y)
    {
        return this with
        {
            X = X + x,
            Y = Y + y
        };
    }

    public PixelRectangle Snap(PixelRectangle bounds)
    {
        double left = Math.Clamp(Math.Round(X), bounds.X, bounds.Right - 1);
        double top = Math.Clamp(Math.Round(Y), bounds.Y, bounds.Bottom - 1);
        double right = Math.Clamp(Math.Round(Right), left + 1, bounds.Right);
        double bottom = Math.Clamp(Math.Round(Bottom), top + 1, bounds.Bottom);
        return new PixelRectangle(left, top, right - left, bottom - top);
    }
}
