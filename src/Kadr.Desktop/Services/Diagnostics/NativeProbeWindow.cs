using System.ComponentModel;
using System.Runtime.InteropServices;

using Kadr.Desktop.Services.Geometry;
using SkiaSharp;

namespace Kadr.Desktop.Services.Diagnostics;

internal sealed class NativeProbeWindow : IDisposable
{
    internal nint Handle
    {
        get;
    }
    internal nint DeviceContext
    {
        get;
    }

    private readonly PixelRectangle _bounds;
    private bool _disposed;

    private NativeProbeWindow(PixelRectangle bounds, bool visible)
    {
        _bounds = bounds;
        Handle = CreateWindowExW(visible ? 0x08000088u : 0, "STATIC", string.Empty, visible ? 0x90000007u : 0x80000000,
            (int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height, 0, 0, 0, 0);

        if (Handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The native rendering test window could not be created.");
        }

        DeviceContext = GetDC(Handle);

        if (DeviceContext == 0)
        {
            DestroyWindow(Handle);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The native rendering test device context could not be acquired.");
        }

        if (visible)
        {
            UpdateWindow(Handle);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            ReleaseDC(Handle, DeviceContext);
            DestroyWindow(Handle);
        }
    }

    internal static NativeProbeWindow Create(PixelRectangle bounds, bool visible)
    {
        return new NativeProbeWindow(bounds, visible);
    }

    internal void Paint(SKColor color)
    {
        nint brush = CreateSolidBrush((uint)(color.Red | (color.Green << 8) | (color.Blue << 16)));

        if (brush == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The native capture test brush could not be created.");
        }

        try
        {
            NativeRectangle rectangle = new()
            {
                Right = (int)_bounds.Width,
                Bottom = (int)_bounds.Height
            };
            FillRect(DeviceContext, ref rectangle, brush);
            GdiFlush();
        }
        finally
        {
            DeleteObject(brush);
        }

        Marshal.ThrowExceptionForHR(DwmFlush());
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint deviceContext);
    [DllImport("user32.dll")] private static extern int FillRect(nint deviceContext, ref NativeRectangle rectangle, nint brush);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }
}
