namespace Kadr.Desktop.Services.Geometry;

public static class DrawingConstraints
{
    public static PixelPoint SnapSquare(PixelPoint start, PixelPoint point)
    {
        double deltaX = point.X - start.X;
        double deltaY = point.Y - start.Y;
        double size = Math.Max(1, (Math.Abs(deltaX) + Math.Abs(deltaY)) / 2);
        return new PixelPoint(start.X + Math.CopySign(size, deltaX), start.Y + Math.CopySign(size, deltaY));
    }

    public static PixelPoint SnapDirection(PixelPoint start, PixelPoint point)
    {
        double deltaX = point.X - start.X;
        double deltaY = point.Y - start.Y;
        double direction = Math.Atan2(deltaY, deltaX);
        double snapped = Math.Round(direction / (Math.PI / 4)) * Math.PI / 4;
        double projected = deltaX * Math.Cos(snapped) + deltaY * Math.Sin(snapped);
        return new PixelPoint(start.X + Math.Cos(snapped) * projected, start.Y + Math.Sin(snapped) * projected);
    }
}
