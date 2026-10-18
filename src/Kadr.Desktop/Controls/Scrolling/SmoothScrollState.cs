using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia;

namespace Kadr.Desktop.Controls.Scrolling;

internal sealed class SmoothScrollState
{
    internal bool IsRunning => _isRunning;
    internal Vector TargetOffset => _targetOffset;

    private const double CompletionDistance = 0.01d;
    private const double CompletionVelocity = 0.5d;
    private const double FallbackFrameMilliseconds = 16d;
    private const double MaximumFrameMilliseconds = 32d;
    private const double MinimumSmoothSeconds = 0.001d;
    private const double SettlingDivisor = 3d;

    private static readonly TimeSpan FallbackFrameInterval =
        TimeSpan.FromMilliseconds(FallbackFrameMilliseconds);
    private static readonly TimeSpan MaximumFrameInterval =
        TimeSpan.FromMilliseconds(MaximumFrameMilliseconds);

    private readonly ScrollViewer _scrollViewer;
    private readonly DispatcherTimer _fallbackTimer;
    private Vector _targetOffset;
    private Vector _velocity;
    private TimeSpan _lastFrameTime;
    private TimeSpan _duration;
    private bool _hasFrameTime;
    private bool _isDisposed;
    private bool _isFrameRequested;
    private bool _isRunning;
    private bool _isSettingOffset;
    private int _frameRequestVersion;

    internal SmoothScrollState(ScrollViewer scrollViewer)
    {
        ArgumentNullException.ThrowIfNull(scrollViewer);

        _scrollViewer = scrollViewer;
        _fallbackTimer = new DispatcherTimer
        {
            Interval = FallbackFrameInterval
        };
        _fallbackTimer.Tick += OnFallbackTick;
        _scrollViewer.PropertyChanged += OnScrollPropertyChanged;
    }

    internal Vector GetBaseOffset()
    {
        if (_isRunning)
        {
            return _targetOffset;
        }

        return _scrollViewer.Offset;
    }

    internal void Start(Vector targetOffset, TimeSpan duration)
    {
        _targetOffset = ClampOffset(targetOffset);
        _duration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero;

        if (_duration == TimeSpan.Zero)
        {
            CompleteAtTarget();
            return;
        }

        if (!_isRunning)
        {
            _isRunning = true;
            _hasFrameTime = false;
            RequestNextFrame();
        }
    }

    internal void Stop()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _isRunning = false;
        _isFrameRequested = false;
        _frameRequestVersion++;
        _fallbackTimer.Stop();
        _fallbackTimer.Tick -= OnFallbackTick;
        _scrollViewer.PropertyChanged -= OnScrollPropertyChanged;
    }

    internal void CancelAnimation()
    {
        Complete();
    }

    private Vector ClampOffset(Vector offset)
    {
        return new Vector(Math.Clamp(offset.X, 0, Math.Max(0, _scrollViewer.Extent.Width - _scrollViewer.Viewport.Width)),
            Math.Clamp(offset.Y, 0, Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height)));
    }

    private void OnScrollPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs arguments)
    {
        if (arguments.Property == ScrollViewer.OffsetProperty && !_isSettingOffset && _isRunning)
        {
            Complete();
        }
        else if (arguments.Property == ScrollViewer.ExtentProperty || arguments.Property == ScrollViewer.ViewportProperty)
        {
            _targetOffset = ClampOffset(_targetOffset);
        }
    }

    private void SetOffset(Vector offset)
    {
        _isSettingOffset = true;
        try
        {
            _scrollViewer.Offset = offset;
        }
        finally
        {
            _isSettingOffset = false;
        }
    }

    private static bool IsClose(Vector current, Vector target, Vector velocity)
    {
        return Math.Abs(current.X - target.X) <= CompletionDistance
            && Math.Abs(current.Y - target.Y) <= CompletionDistance
            && Math.Abs(velocity.X) <= CompletionVelocity
            && Math.Abs(velocity.Y) <= CompletionVelocity;
    }

    private static double SmoothDamp(
        double current,
        double target,
        double currentVelocity,
        double smoothSeconds,
        double deltaSeconds,
        out double nextVelocity)
    {
        double omega = 2d / smoothSeconds;
        double x = omega * deltaSeconds;
        double exponential = 1d / (1d + x + (0.48d * x * x) + (0.235d * x * x * x));
        double change = current - target;
        double temp = (currentVelocity + (omega * change)) * deltaSeconds;
        nextVelocity = (currentVelocity - (omega * temp)) * exponential;

        return target + ((change + temp) * exponential);
    }

    private static TimeSpan ClampDelta(TimeSpan delta)
    {
        if (delta <= MaximumFrameInterval)
        {
            return delta;
        }

        return MaximumFrameInterval;
    }

    private void Complete()
    {
        _velocity = default;
        _isRunning = false;
        _isFrameRequested = false;
        _hasFrameTime = false;
        _frameRequestVersion++;
        _fallbackTimer.Stop();
    }

    private void CompleteAtTarget()
    {
        SetOffset(_targetOffset);
        Complete();
    }

    private TimeSpan GetDelta(TimeSpan frameTime)
    {
        if (!_hasFrameTime)
        {
            _lastFrameTime = frameTime;
            _hasFrameTime = true;
            return FallbackFrameInterval;
        }

        TimeSpan delta = frameTime - _lastFrameTime;
        _lastFrameTime = frameTime;

        if (delta <= TimeSpan.Zero)
        {
            return FallbackFrameInterval;
        }

        return delta;
    }

    private void OnAnimationFrame(TimeSpan frameTime, int frameRequestVersion)
    {
        if (frameRequestVersion != _frameRequestVersion)
        {
            return;
        }

        _isFrameRequested = false;

        if (!_isRunning || _isDisposed)
        {
            return;
        }

        Step(frameTime);
        RequestNextFrame();
    }

    private void OnFallbackTick(object? sender, EventArgs e)
    {
        if (!_isRunning || _isDisposed)
        {
            return;
        }

        Step(_lastFrameTime + FallbackFrameInterval);
    }

    private void RequestNextFrame()
    {
        if (!_isRunning || _isFrameRequested || _isDisposed)
        {
            return;
        }

        TopLevel? topLevel = TopLevel.GetTopLevel(_scrollViewer);

        if (topLevel is not null)
        {
            _isFrameRequested = true;
            int frameRequestVersion = ++_frameRequestVersion;
            topLevel.RequestAnimationFrame(
                frameTime => OnAnimationFrame(frameTime, frameRequestVersion));
            return;
        }

        if (!_fallbackTimer.IsEnabled)
        {
            _fallbackTimer.Start();
        }
    }

    private void Step(TimeSpan frameTime)
    {
        TimeSpan delta = ClampDelta(GetDelta(frameTime));
        double smoothSeconds = Math.Max(
            MinimumSmoothSeconds,
            _duration.TotalSeconds / SettlingDivisor);
        Vector currentOffset = _scrollViewer.Offset;
        double velocityX = _velocity.X;
        double velocityY = _velocity.Y;
        double x = SmoothDamp(
            currentOffset.X,
            _targetOffset.X,
            velocityX,
            smoothSeconds,
            delta.TotalSeconds,
            out double nextVelocityX);
        double y = SmoothDamp(
            currentOffset.Y,
            _targetOffset.Y,
            velocityY,
            smoothSeconds,
            delta.TotalSeconds,
            out double nextVelocityY);

        _velocity = new Vector(nextVelocityX, nextVelocityY);
        Vector nextOffset = new(x, y);
        SetOffset(nextOffset);

        if (IsClose(nextOffset, _targetOffset, _velocity))
        {
            Complete();
        }
    }
}
