using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Kadr.Desktop.Controls;

internal sealed class ScreenshotDrawOperation : ICustomDrawOperation
{
    public Rect Bounds
    {
        get;
    }

    private readonly SKPicture _scene;
    private readonly float _originX;
    private readonly float _originY;
    private readonly float _scale;
    private readonly Action? _frameRendered;

    internal ScreenshotDrawOperation(Rect bounds, SKPicture scene, double originX, double originY, double scale, Action? frameRendered = null)
    {
        Bounds = bounds;
        _scene = scene;
        _originX = (float)originX;
        _originY = (float)originY;
        _scale = (float)scale;
        _frameRendered = frameRendered;
    }

    public bool HitTest(Point point)
    {
        return Bounds.Contains(point);
    }

    public bool Equals(ICustomDrawOperation? other)
    {
        return ReferenceEquals(this, other);
    }

    public void Render(ImmediateDrawingContext context)
    {
        ISkiaSharpApiLeaseFeature? feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();

        if (feature is null)
        {
            return;
        }

        using ISkiaSharpApiLease lease = feature.Lease();
        SKCanvas canvas = lease.SkCanvas;
        canvas.Save();
        canvas.Scale(_scale);
        canvas.Translate(-_originX, -_originY);
        canvas.DrawPicture(_scene);
        canvas.Restore();
        _frameRendered?.Invoke();
    }

    public void Dispose()
    {
        _scene.Dispose();
    }
}
