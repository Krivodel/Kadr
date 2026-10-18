using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using SkiaSharp;

namespace Kadr.Desktop.Services.Rendering;

internal static class CurveGeometry
{
    internal static SKPath CreatePath(DrawingElement element)
    {
        SKPath path = new();
        PixelPoint start = element.Points[0];
        PixelPoint end = element.Points[^1];
        path.MoveTo((float)start.X, (float)start.Y);
        if (element.Bend is PixelPoint bend)
        {
            path.QuadTo((float)(2 * bend.X - (start.X + end.X) / 2),
                (float)(2 * bend.Y - (start.Y + end.Y) / 2), (float)end.X, (float)end.Y);
        }
        else
        {
            path.LineTo((float)end.X, (float)end.Y);
        }
        return path;
    }
}
