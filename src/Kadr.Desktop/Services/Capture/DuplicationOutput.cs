using Kadr.Desktop.Services.Geometry;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Kadr.Desktop.Services.Capture;

internal sealed class DuplicationOutput : IMonitorCapture
{
    public DesktopMonitor Monitor
    {
        get;
    }
    public bool HasFrame => _latest is not null;
    public bool IsReadyForIdle => HasFrame && _ownsFrame;
    public CapturedCursor? Cursor
    {
        get; private set;
    }
    public bool CanToggleCursor { get; private set; } = true;

    private const int WaitTimeout = unchecked((int)0x887A0027);
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGIOutputDuplication _duplication;
    private readonly int _rotation;
    private ID3D11Texture2D? _latest;
    private long _presentedAt;
    private bool _ownsFrame;

    internal DuplicationOutput(IDXGIAdapter1 adapter, IDXGIOutput output)
    {
        OutputDescription description = output.Description;
        Monitor = MonitorDescription.Read(output);
        _rotation = (int)description.Rotation;
        CaptureDeviceFactory.Create(adapter, out _device, out _context);

        try
        {
            using IDXGIOutput5 output5 = output.QueryInterface<IDXGIOutput5>();
            _duplication = output5.DuplicateOutput1(_device,
                new Format[] { Format.B8G8R8A8_UNorm, Format.R16G16B16A16_Float, Format.R10G10B10A2_UNorm });
        }
        catch
        {
            _context.Dispose();
            _device.Dispose();
            throw;
        }
    }

    public void Poll()
    {
        Acquire(0);
    }

    public void PrepareCapture()
    {
        ReleaseHeldFrame();
    }

    public void RequestFrame(int timeoutMilliseconds)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        int remaining = timeoutMilliseconds;

        while (!Acquire(remaining))
        {
            remaining = timeoutMilliseconds - (int)Math.Ceiling(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);

            if (remaining <= 0)
            {
                return;
            }
        }
    }

    public FrozenOutput Freeze()
    {
        ID3D11Texture2D latest = _latest ?? throw new InvalidOperationException("No desktop frame is available.");
        Texture2DDescription description = latest.Description;
        description.Usage = ResourceUsage.Staging;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        ID3D11Texture2D staging = _device.CreateTexture2D(description);
        _context.CopyResource(staging, latest);
        return new FrozenOutput(staging, _context, Monitor, _rotation, _presentedAt);
    }

    public void Dispose()
    {
        if (_ownsFrame)
        {
            _duplication.ReleaseFrame();
            _ownsFrame = false;
        }

        _latest?.Dispose();
        _duplication.Dispose();
        _context.Dispose();
        _device.Dispose();
    }

    private void ReleaseHeldFrame()
    {
        if (_ownsFrame)
        {
            _ownsFrame = false;
            _duplication.ReleaseFrame().CheckError();
        }
    }

    private bool Acquire(int timeoutMilliseconds)
    {
        ReleaseHeldFrame();
        SharpGen.Runtime.Result result = _duplication.AcquireNextFrame((uint)timeoutMilliseconds, out OutduplFrameInfo information,
            out IDXGIResource resource);

        if (result.Code == WaitTimeout)
        {
            return false;
        }

        result.CheckError();
        _ownsFrame = true;

        using (resource)
        using (ID3D11Texture2D texture = resource.QueryInterface<ID3D11Texture2D>())
        {
            if (information.LastPresentTime != 0)
            {
                if ((_latest is null) || (_latest.Description.Format != texture.Description.Format)
                    || (_latest.Description.Width != texture.Description.Width)
                    || (_latest.Description.Height != texture.Description.Height))
                {
                    _latest?.Dispose();
                    Texture2DDescription description = texture.Description;
                    description.BindFlags = BindFlags.None;
                    description.CPUAccessFlags = CpuAccessFlags.None;
                    description.Usage = ResourceUsage.Default;
                    description.MiscFlags = ResourceOptionFlags.None;
                    _latest = _device.CreateTexture2D(description);
                }

                _context.CopyResource(_latest, texture);
                _presentedAt = information.LastPresentTime;
            }

            UpdateCursor(information);
        }
        return information.LastPresentTime != 0;
    }

    private unsafe void UpdateCursor(OutduplFrameInfo information)
    {
        if (information.LastMouseUpdateTime == 0)
        {
            return;
        }

        if (information.PointerPosition.Visible)
        {
            PixelPoint position = new(Monitor.Bounds.X + information.PointerPosition.Position.X,
                Monitor.Bounds.Y + information.PointerPosition.Position.Y);

            if (information.PointerShapeBufferSize > 0)
            {
                byte[] bytes = new byte[information.PointerShapeBufferSize];

                fixed (byte* buffer = bytes)
                {
                    _duplication.GetFramePointerShape((uint)bytes.Length, (nint)buffer, out _,
                        out OutduplPointerShapeInfo shape).CheckError();
                    CursorShapeKind kind = (int)shape.Type == 1 ? CursorShapeKind.Monochrome
                        : (int)shape.Type == 4 ? CursorShapeKind.MaskedColor : CursorShapeKind.Color;
                    Cursor = new CapturedCursor(kind, position, (int)shape.Width,
                        (int)shape.Height / (kind == CursorShapeKind.Monochrome ? 2 : 1),
                        (int)shape.Pitch, bytes, information.LastMouseUpdateTime);
                }
            }
            else if (Cursor is CapturedCursor cursor)
            {
                Cursor = cursor with
                {
                    Position = position,
                    UpdatedAt = information.LastMouseUpdateTime
                };
            }
        }
        else if (NativeDesktop.IsPointerOnMonitor(Monitor))
        {
            Cursor = null;
            CanToggleCursor = !NativeDesktop.IsSystemCursorVisible();
        }
    }
}
