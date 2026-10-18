using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Kadr.Desktop.Services.Capture;

[SupportedOSPlatform("windows10.0.19041")]
internal sealed class WindowsGraphicsOutput : IMonitorCapture
{
    public DesktopMonitor Monitor
    {
        get;
    }
    public bool HasFrame => _latest is not null;
    public CapturedCursor? Cursor => null;
    public bool CanToggleCursor => true;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDirect3DDevice _runtimeDevice;
    private readonly GraphicsCaptureItem _item;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private ID3D11Texture2D? _latest;
    private long _presentedAt;

    internal WindowsGraphicsOutput(DesktopMonitor monitor)
    {
        Monitor = monitor;
        CaptureDeviceFactory.Create(null, out _device, out _context);
        try
        {
            using (ID3D11Multithread multithread = _device.QueryInterface<ID3D11Multithread>())
            {
                multithread.SetMultithreadProtected(true);
            }
            using IDXGIDevice dxgiDevice = _device.QueryInterface<IDXGIDevice>();
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out nint runtime));

            try
            {
                _runtimeDevice = MarshalInterface<IDirect3DDevice>.FromAbi(runtime);
            }
            finally
            {
                Marshal.Release(runtime);
            }

            _item = CreateItem(monitor.Handle);
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_runtimeDevice,
                monitor.IsHdr ? DirectXPixelFormat.R16G16B16A16Float : DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _item.Size);
            _session = _pool.CreateCaptureSession(_item);
            _session.IsCursorCaptureEnabled = false;

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
            {
                _session.IsBorderRequired = false;
            }

            _session.StartCapture();
        }
        catch
        {
            _session?.Dispose();
            _pool?.Dispose();
            _runtimeDevice?.Dispose();
            _context.Dispose();
            _device.Dispose();
            throw;
        }
    }

    public void Poll()
    {
        if ((_item.Size.Width != (int)Monitor.Bounds.Width) || (_item.Size.Height != (int)Monitor.Bounds.Height))
        {
            throw new InvalidOperationException("The display configuration changed during capture.");
        }

        CopyFrame(_pool);
    }

    public FrozenOutput Freeze()
    {
        ID3D11Texture2D latest = _latest ?? throw new InvalidOperationException("No graphics capture frame is available.");
        Texture2DDescription description = latest.Description;
        description.Usage = ResourceUsage.Staging;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        ID3D11Texture2D staging = _device.CreateTexture2D(description);
        _context.CopyResource(staging, latest);
        return new FrozenOutput(staging, _context, Monitor, 1, _presentedAt);
    }

    public void Dispose()
    {
        _session.Dispose();
        _pool.Dispose();
        _latest?.Dispose();
        _context.Dispose();
        _runtimeDevice.Dispose();
        _device.Dispose();
    }

    private static GraphicsCaptureItem CreateItem(nint monitor)
    {
        const string RuntimeClass = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Guid factoryId = typeof(IGraphicsCaptureItemInterop).GUID;
        Marshal.ThrowExceptionForHR(WindowsCreateString(RuntimeClass, RuntimeClass.Length, out nint className));

        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, ref factoryId, out IGraphicsCaptureItemInterop factory));

            try
            {
                Guid itemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                factory.CreateForMonitor(monitor, ref itemId, out nint item);

                try
                {
                    return MarshalInterface<GraphicsCaptureItem>.FromAbi(item);
                }
                finally
                {
                    Marshal.Release(item);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        finally
        {
            WindowsDeleteString(className);
        }
    }

    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint result);
    [DllImport("combase.dll")] private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string source, int length, out nint value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, ref Guid identifier, out IGraphicsCaptureItemInterop factory);

    private void CopyFrame(Direct3D11CaptureFramePool sender)
    {
        using Direct3D11CaptureFrame frame = sender.TryGetNextFrame();

        if (frame is null)
        {
            return;
        }

        IDirect3DDxgiInterfaceAccess access = frame.Surface.As<IDirect3DDxgiInterfaceAccess>();
        Guid textureId = typeof(ID3D11Texture2D).GUID;
        access.GetInterface(ref textureId, out nint pointer);
        using ID3D11Texture2D texture = new(pointer);

        if ((frame.ContentSize.Width != (int)Monitor.Bounds.Width) || (frame.ContentSize.Height != (int)Monitor.Bounds.Height))
        {
            throw new InvalidOperationException("The captured monitor size changed during capture.");
        }

        if (_latest is null)
        {
            Texture2DDescription description = texture.Description;
            description.Width = (uint)frame.ContentSize.Width;
            description.Height = (uint)frame.ContentSize.Height;
            description.BindFlags = BindFlags.None;
            description.MiscFlags = ResourceOptionFlags.None;
            _latest = _device.CreateTexture2D(description);
        }

        _context.CopySubresourceRegion(_latest, 0, 0, 0, 0, texture, 0,
            new Vortice.Mathematics.Box(0, 0, 0, frame.ContentSize.Width, frame.ContentSize.Height, 1));
        _presentedAt = (long)(frame.SystemRelativeTime.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);
    }

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        void CreateForWindow(nint window, ref Guid identifier, out nint item);
        void CreateForMonitor(nint monitor, ref Guid identifier, out nint item);
    }

    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        void GetInterface(ref Guid identifier, out nint result);
    }
}
