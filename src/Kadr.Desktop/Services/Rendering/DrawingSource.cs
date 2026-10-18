using SkiaSharp;

namespace Kadr.Desktop.Services.Rendering;

public sealed class DrawingSource : IDisposable
{
    public SKPicture Picture
    {
        get;
    }
    public SKRect Bounds => new(0, 0, _info.Width, _info.Height);

    private readonly SKImageInfo _info;
    private SKImage? _blurred;
    private float _sigma;
    private bool _disposed;

    public DrawingSource(SKPicture picture, SKImageInfo info)
    {
        Picture = picture ?? throw new ArgumentNullException(nameof(picture));
        _info = info;
    }

    public SKImage GetBlurredImage(float sigma)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((_blurred is not null) && (_sigma == sigma))
        {
            return _blurred;
        }

        int reduction = Math.Clamp((int)(sigma / 4), 1, 16);
        SKImageInfo reduced = new((int)Math.Ceiling((double)_info.Width / reduction),
            (int)Math.Ceiling((double)_info.Height / reduction), SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKSurface input = SKSurface.Create(reduced);
        input.Canvas.Clear(SKColors.Transparent);
        input.Canvas.Scale((float)reduced.Width / _info.Width, (float)reduced.Height / _info.Height);
        input.Canvas.DrawPicture(Picture);
        using SKImage image = input.Snapshot();
        using SKSurface filtered = SKSurface.Create(reduced);
        filtered.Canvas.Clear(SKColors.Transparent);
        using SKImageFilter filter = SKImageFilter.CreateBlur(sigma / reduction, sigma / reduction, SKShaderTileMode.Clamp);
        using SKPaint paint = new()
        {
            ImageFilter = filter
        };
        filtered.Canvas.DrawImage(image, 0, 0, paint);
        SKImage result = filtered.Snapshot();
        _blurred?.Dispose();
        _blurred = result;
        _sigma = sigma;
        return result;
    }

    public void Dispose()
    {
        _disposed = true;
        _blurred?.Dispose();
        _blurred = null;
    }
}
