namespace Kadr.Desktop.Services.Geometry;

public readonly record struct PixelPoint(double X, double Y)
{
    public double DistanceTo(PixelPoint other)
    {
        return Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));
    }

    public double DistanceToSegment(PixelPoint start, PixelPoint end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double lengthSquared = (dx * dx) + (dy * dy);
        double progress = lengthSquared == 0 ? 0 : Math.Clamp((((X - start.X) * dx) + ((Y - start.Y) * dy)) / lengthSquared, 0, 1);
        return DistanceTo(new PixelPoint(start.X + (progress * dx), start.Y + (progress * dy)));
    }
}
