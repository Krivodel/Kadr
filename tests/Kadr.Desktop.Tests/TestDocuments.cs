using System.Collections.Immutable;
using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using SkiaSharp;

namespace Kadr.Desktop.Tests;

internal static class TestDocuments
{
    internal static DrawingDocument Create(int width = 160, int height = 120, SKColor? background = null)
    {
        SKBitmap image = new(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        image.Erase(background ?? SKColors.White);
        DesktopMonitor monitor = new(1, new PixelRectangle(0, 0, width, height), "Test monitor");
        CapturedDesktop capture = new(image, new DesktopMonitor[] { monitor }, monitor.Bounds, null, true,
            new CaptureMetrics(0, 0, 0, Array.Empty<long>()));
        return new DrawingDocument(capture, false);
    }

    internal static DrawingElement Element(DrawingToolId tool, double x, double y, double right, double bottom)
    {
        return new DrawingElement(Guid.NewGuid(), tool, ImmutableList.Create(new PixelPoint(x, y),
            new PixelPoint(right, bottom)), 0xffee2233, 3);
    }
}
