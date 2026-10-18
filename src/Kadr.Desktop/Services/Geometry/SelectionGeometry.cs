namespace Kadr.Desktop.Services.Geometry;

public static class SelectionGeometry
{
    public static ResizeHandle HitSelectionEdge(PixelRectangle rectangle, PixelPoint point, double radius)
    {
        ResizeHandle outside = ResizeHandle.None;

        if (point.X < rectangle.X)
        {
            outside |= ResizeHandle.Left;
        }
        else if (point.X > rectangle.Right)
        {
            outside |= ResizeHandle.Right;
        }

        if (point.Y < rectangle.Y)
        {
            outside |= ResizeHandle.Top;
        }
        else if (point.Y > rectangle.Bottom)
        {
            outside |= ResizeHandle.Bottom;
        }

        return outside != ResizeHandle.None ? outside : HitHandle(rectangle, point, radius);
    }

    public static PixelPoint SnapPointerToBoundary(PixelPoint point, PixelRectangle monitor)
    {
        return new PixelPoint(point.X >= monitor.Right - 1 ? monitor.Right : point.X,
            point.Y >= monitor.Bottom - 1 ? monitor.Bottom : point.Y);
    }

    public static ResizeHandle HitHandle(PixelRectangle rectangle, PixelPoint point, double radius)
    {
        if (rectangle.IsEmpty || !new PixelRectangle(rectangle.X - radius, rectangle.Y - radius,
                rectangle.Width + (radius * 2), rectangle.Height + (radius * 2)).Contains(point))
        {
            return ResizeHandle.None;
        }

        ResizeHandle handle = ResizeHandle.None;

        if (Math.Abs(point.X - rectangle.X) <= radius)
        {
            handle |= ResizeHandle.Left;
        }
        else if (Math.Abs(point.X - rectangle.Right) <= radius)
        {
            handle |= ResizeHandle.Right;
        }

        if (Math.Abs(point.Y - rectangle.Y) <= radius)
        {
            handle |= ResizeHandle.Top;
        }
        else if (Math.Abs(point.Y - rectangle.Bottom) <= radius)
        {
            handle |= ResizeHandle.Bottom;
        }

        return handle;
    }

    public static PixelRectangle Resize(PixelRectangle rectangle, ResizeHandle handle, PixelPoint point)
    {
        double left = handle.HasFlag(ResizeHandle.Left) ? point.X : rectangle.X;
        double right = handle.HasFlag(ResizeHandle.Right) ? point.X : rectangle.Right;
        double top = handle.HasFlag(ResizeHandle.Top) ? point.Y : rectangle.Y;
        double bottom = handle.HasFlag(ResizeHandle.Bottom) ? point.Y : rectangle.Bottom;
        PixelRectangle result = PixelRectangle.FromPoints(new PixelPoint(left, top), new PixelPoint(right, bottom));
        return result with
        {
            Width = Math.Max(1, result.Width),
            Height = Math.Max(1, result.Height)
        };
    }
}
