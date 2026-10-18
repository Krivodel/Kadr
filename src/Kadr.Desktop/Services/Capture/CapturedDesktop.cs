using Kadr.Desktop.Services.Geometry;
using SkiaSharp;

namespace Kadr.Desktop.Services.Capture;

public sealed class CapturedDesktop : IDisposable
{
    public SKBitmap Image
    {
        get;
    }
    public IReadOnlyList<DesktopMonitor> Monitors
    {
        get;
    }
    public PixelRectangle Bounds
    {
        get;
    }
    public CapturedCursor? Cursor
    {
        get;
    }
    public bool CanToggleCursor
    {
        get;
    }
    public CaptureMetrics Metrics
    {
        get;
    }

    public CapturedDesktop(SKBitmap image, IReadOnlyList<DesktopMonitor> monitors,
        PixelRectangle bounds, CapturedCursor? cursor, bool canToggleCursor, CaptureMetrics metrics)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
        Image.SetImmutable();
        Monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        Bounds = bounds;
        Cursor = cursor;
        CanToggleCursor = canToggleCursor;
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public void Dispose()
    {
        Image.Dispose();
    }
}
