using System.Collections.Concurrent;
using System.Diagnostics;
using Kadr.Desktop.Services.Geometry;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Kadr.Desktop.Services.Capture;

internal sealed class DesktopCaptureService : ICaptureService
{
    private const int MaximumUpdateWaitMilliseconds = 20;
    private const int IdleFrameRetryMilliseconds = 100;

    private bool KeepsSessionReady => (_backend == CaptureBackend.DesktopDuplication) || (_mode == CaptureMode.Continuous);
    private readonly MonitorCaptureFactory _factory;
    private readonly ILogger<DesktopCaptureService> _logger;
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private IReadOnlyList<IMonitorCapture> _outputs = Array.Empty<IMonitorCapture>();
    private CaptureBackend _backend;
    private CaptureMode _mode = CaptureMode.OnDemand;
    private long _retryAt;
    private long _nextTopologyCheck;
    private bool _configured;

    internal DesktopCaptureService(MonitorCaptureFactory factory, ILogger<DesktopCaptureService> logger)
    {
        _factory = factory;
        _logger = logger;
        _worker = Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public Task ConfigureAsync(CaptureBackend backend, CaptureMode mode, CancellationToken ct)
    {
        return EnqueueAsync(() =>
        {
            if (!_configured || (_backend != backend)
                || ((backend == CaptureBackend.WindowsGraphicsCapture) && (_mode != mode)))
            {
                CloseOutputs();
            }

            _backend = backend;
            _mode = mode;
            _retryAt = 0;
            _configured = true;
        }, ct);
    }

    public Task<CapturedDesktop> CaptureAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_stop.IsCancellationRequested, this);
        long requestedAt = Stopwatch.GetTimestamp();
        TaskCompletionSource<CapturedDesktop> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Enqueue(() =>
        {
            try
            {
                CapturedDesktop desktop;

                try
                {
                    desktop = CaptureCore(requestedAt, ct);
                }
                catch (SharpGen.Runtime.SharpGenException exception) when (exception.HResult is unchecked((int)0x887A0026)
                    or unchecked((int)0x887A0005) or unchecked((int)0x887A0007))
                {
                    _logger.LogWarning(exception, "Capture resources were invalidated; recreating the selected backend");
                    CloseOutputs();
                    desktop = CaptureCore(requestedAt, ct);
                }

                completion.SetResult(desktop);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Desktop capture failed");
                completion.SetException(exception);
                CloseOutputs();
            }
            finally
            {
                if (!KeepsSessionReady)
                {
                    CloseOutputs();
                }
            }
        });
        _wake.Set();
        return completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _wake.Set();
        await _worker.ConfigureAwait(false);
        _stop.Dispose();
        _wake.Dispose();
    }

    private CapturedDesktop CaptureCore(long requestedAt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _stop.Token.ThrowIfCancellationRequested();
        EnsureOutputs();

        if (_mode == CaptureMode.OnDemand)
        {
            foreach (IMonitorCapture output in _outputs)
            {
                output.PrepareCapture();
            }
        }

        long updateWaitStarted = Stopwatch.GetTimestamp();

        foreach (IMonitorCapture output in _outputs)
        {
            if (_mode == CaptureMode.OnDemand)
            {
                int remaining = Math.Max(0, MaximumUpdateWaitMilliseconds
                    - (int)Math.Ceiling(Stopwatch.GetElapsedTime(updateWaitStarted).TotalMilliseconds));
                output.RequestFrame(remaining);
            }
            else
            {
                output.Poll();
            }
        }
        Stopwatch deadline = Stopwatch.StartNew();

        while (_outputs.Any(output => !output.HasFrame))
        {
            ct.ThrowIfCancellationRequested();
            _stop.Token.ThrowIfCancellationRequested();

            if (deadline.Elapsed > TimeSpan.FromSeconds(3))
            {
                throw new TimeoutException("A desktop capture did not produce a frame within three seconds.");
            }

            Poll();

            if (_outputs.Any(output => !output.HasFrame))
            {
                _wake.WaitOne(1);
            }
        }

        return FreezeDesktop(requestedAt);
    }

    private Task EnqueueAsync(Action command, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_stop.IsCancellationRequested, this);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Enqueue(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                _stop.Token.ThrowIfCancellationRequested();
                command();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        _wake.Set();
        return completion.Task;
    }

    private void Run()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                while (_commands.TryDequeue(out Action? command))
                {
                    command();
                }

                if (_configured && KeepsSessionReady && (Stopwatch.GetTimestamp() >= _retryAt))
                {
                    try
                    {
                        EnsureOutputs();
                        if ((_mode == CaptureMode.Continuous) || _outputs.Any(output => !output.IsReadyForIdle))
                        {
                            Poll();
                        }
                    }
                    catch (Exception exception)
                    {
                        _logger.LogWarning(exception, "Desktop capture will reconnect after a display or session change");
                        CloseOutputs();
                        _retryAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
                    }
                }

                int wait = Timeout.Infinite;

                if (_configured && KeepsSessionReady)
                {
                    wait = _outputs.Count == 0 ? 1000
                        : (_mode == CaptureMode.Continuous) || _outputs.Any(output => !output.HasFrame) ? 4
                        : _outputs.Any(output => !output.IsReadyForIdle) ? IdleFrameRetryMilliseconds : Timeout.Infinite;
                }

                _wake.WaitOne(wait);
            }
        }
        finally
        {
            CloseOutputs();

            while (_commands.TryDequeue(out Action? command))
            {
                command();
            }
        }
    }

    private void EnsureOutputs()
    {
        if (_outputs.Count == 0)
        {
            _outputs = _factory.Create(_backend);
        }

        long now = Stopwatch.GetTimestamp();

        if (now < _nextTopologyCheck)
        {
            return;
        }

        _nextTopologyCheck = now + Stopwatch.Frequency;

        IReadOnlyList<DesktopMonitor> monitors = NativeDesktop.GetMonitors();

        if ((_outputs.Count != monitors.Count) || _outputs.Any(output => !monitors.Any(monitor =>
                (monitor.Handle == output.Monitor.Handle) && (monitor.Bounds == output.Monitor.Bounds))))
        {
            CloseOutputs();
            _outputs = _factory.Create(_backend);
        }
    }

    private void Poll()
    {
        foreach (IMonitorCapture output in _outputs)
        {
            if ((_mode == CaptureMode.Continuous) || !output.IsReadyForIdle)
            {
                output.Poll();
            }
        }
    }

    private CapturedDesktop FreezeDesktop(long requestedAt)
    {
        List<FrozenOutput> frozen = [];

        try
        {
            CapturedCursor? cursor = _backend == CaptureBackend.WindowsGraphicsCapture
                ? NativeDesktop.CaptureCursor()
                : _outputs.Where(output => NativeDesktop.IsPointerOnMonitor(output.Monitor))
                    .Select(output => output.Cursor).OfType<CapturedCursor>().MaxBy(value => value.UpdatedAt);
            bool canToggle = _outputs.All(output => output.CanToggleCursor);

            foreach (IMonitorCapture output in _outputs)
            {
                frozen.Add(output.Freeze());
            }

            long frozenAt = Stopwatch.GetTimestamp();
            double left = frozen.Min(output => output.Monitor.Bounds.X);
            double top = frozen.Min(output => output.Monitor.Bounds.Y);
            PixelRectangle bounds = new(left, top, frozen.Max(output => output.Monitor.Bounds.Right) - left,
                frozen.Max(output => output.Monitor.Bounds.Bottom) - top);
            SKBitmap image = frozen.Count == 1 ? TextureReadback.Read(frozen[0])
                : new SKBitmap((int)bounds.Width, (int)bounds.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

            try
            {
                if (frozen.Count > 1)
                {
                    using SKCanvas canvas = new(image);
                    canvas.Clear(SKColors.Transparent);

                    foreach (FrozenOutput output in frozen)
                    {
                        using SKBitmap tile = TextureReadback.Read(output);
                        canvas.DrawBitmap(tile, (float)(output.Monitor.Bounds.X - left), (float)(output.Monitor.Bounds.Y - top));
                    }
                }

                CaptureMetrics metrics = new(requestedAt, frozenAt, Stopwatch.GetTimestamp(), frozen.Select(output => output.PresentedAt).ToArray());
                _logger.LogInformation("Capture timing: freeze {FreezeMs} ms, readback {ReadbackMs} ms, monitors {MonitorCount}",
                    metrics.FreezeMilliseconds, metrics.ReadbackMilliseconds, frozen.Count);
                return new CapturedDesktop(image, frozen.Select(output => output.Monitor).ToArray(), bounds, cursor, canToggle, metrics);
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }
        finally
        {
            foreach (FrozenOutput output in frozen)
            {
                output.Dispose();
            }
        }
    }

    private void CloseOutputs()
    {
        IReadOnlyList<IMonitorCapture> outputs = _outputs;
        _outputs = Array.Empty<IMonitorCapture>();

        foreach (IMonitorCapture output in outputs)
        {
            try
            {
                output.Dispose();
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Capture resources could not be released for monitor {MonitorName}", output.Monitor.Name);
            }
        }
    }
}
