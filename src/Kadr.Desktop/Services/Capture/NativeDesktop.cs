using System.Runtime.InteropServices;

using Kadr.Desktop.Services.Geometry;

namespace Kadr.Desktop.Services.Capture;

internal static class NativeDesktop
{
    internal static nint GetSystemCursorHandle()
    {
        CursorInformation information = new() { Size = Marshal.SizeOf<CursorInformation>() };
        return GetCursorInfo(ref information) ? information.Cursor : 0;
    }

    internal static bool IsSystemCursorVisible()
    {
        CursorInformation information = new()
        {
            Size = Marshal.SizeOf<CursorInformation>()
        };
        return GetCursorInfo(ref information) && ((information.Flags & 1) != 0);
    }

    internal static bool IsPointerOnMonitor(DesktopMonitor monitor)
    {
        return monitor.Bounds.Contains(GetPointerPosition());
    }

    internal static PixelPoint GetPointerPosition()
    {
        GetCursorPos(out NativePoint point);
        return new PixelPoint(point.X, point.Y);
    }

    internal static void RestorePointer(PixelPoint position)
    {
        SetCursorPos((int)position.X, (int)position.Y);
    }

    internal static bool IsKeyDown(int key)
    {
        return (GetAsyncKeyState(key) & 0x8000) != 0;
    }

    internal static IReadOnlyList<DesktopMonitor> GetMonitors()
    {
        List<DesktopMonitor> monitors = [];
        MonitorCallback callback = (nint monitor, nint context, ref NativeRectangle rectangle, nint data) =>
        {
            MonitorInformation information = new()
            {
                Size = Marshal.SizeOf<MonitorInformation>(),
                Name = string.Empty
            };

            if (GetMonitorInfoW(monitor, ref information))
            {
                monitors.Add(new DesktopMonitor(monitor,
                    new PixelRectangle(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left,
                        rectangle.Bottom - rectangle.Top), information.Name));
            }

            return true;
        };
        EnumDisplayMonitors(0, 0, callback, 0);
        return monitors.AsReadOnly();
    }

    internal static CapturedCursor? CaptureCursor()
    {
        CursorInformation information = new()
        {
            Size = Marshal.SizeOf<CursorInformation>()
        };

        if (!GetCursorInfo(ref information) || ((information.Flags & 1) == 0))
        {
            return null;
        }

        nint icon = CopyIcon(information.Cursor);

        if (icon == 0)
        {
            return null;
        }

        try
        {
            if (!GetIconInfo(icon, out IconInformation details))
            {
                return null;
            }

            try
            {
                nint bitmap = details.Color != 0 ? details.Color : details.Mask;
                GetObjectW(bitmap, Marshal.SizeOf<NativeBitmap>(), out NativeBitmap description);
                int width = description.Width;
                int height = details.Color != 0 ? description.Height : description.Height / 2;
                int pitch = ((width + 31) / 32) * 4;
                byte[] mask = ReadBitmap(details.Mask, width, height * (details.Color == 0 ? 2 : 1), 1);
                PixelPoint position = new(information.Position.X - details.HotspotX,
                    information.Position.Y - details.HotspotY);

                if (details.Color == 0)
                {
                    return new CapturedCursor(CursorShapeKind.Monochrome, position, width, height,
                        pitch, mask, System.Diagnostics.Stopwatch.GetTimestamp());
                }

                byte[] pixels = ReadBitmap(details.Color, width, height, 32);
                bool hasAlpha = Enumerable.Range(0, width * height).Any(index => pixels[(index * 4) + 3] != 0);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int offset = ((y * width) + x) * 4;

                        if (hasAlpha)
                        {
                            int alpha = pixels[offset + 3];

                            if (alpha > 0)
                            {
                                for (int channel = 0; channel < 3; channel++)
                                {
                                    pixels[offset + channel] = (byte)Math.Min(255, pixels[offset + channel] * 255 / alpha);
                                }
                            }
                        }
                        else
                        {
                            pixels[offset + 3] = (mask[(y * pitch) + (x / 8)] & (0x80 >> (x % 8))) != 0
                                ? (byte)255 : (byte)0;
                        }
                    }
                }

                return new CapturedCursor(hasAlpha ? CursorShapeKind.Color : CursorShapeKind.MaskedColor,
                    position, width, height, width * 4, pixels, System.Diagnostics.Stopwatch.GetTimestamp());
            }
            finally
            {
                if (details.Color != 0)
                {
                    DeleteObject(details.Color);
                }

                DeleteObject(details.Mask);
            }
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    private static byte[] ReadBitmap(nint bitmap, int width, int height, ushort bits)
    {
        int pitch = ((width * bits + 31) / 32) * 4;
        byte[] bytes = new byte[checked(pitch * height)];
        BitmapInformation information = new()
        {
            Size = 40,
            Width = width,
            Height = -height,
            Planes = 1,
            Bits = bits,
            White = 0x00ffffff
        };
        nint context = GetDC(0);

        try
        {
            if (GetDIBits(context, bitmap, 0, (uint)height, bytes, ref information, 0) == 0)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            ReleaseDC(0, context);
        }

        return bytes;
    }

    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInformation information);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint context, nint rectangle, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInformation information);
    [DllImport("user32.dll")] private static extern nint CopyIcon(nint icon);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(nint icon, out IconInformation information);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint context);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint bitmap);
    [DllImport("gdi32.dll")] private static extern int GetObjectW(nint bitmap, int size, out NativeBitmap description);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int GetDIBits(nint context, nint bitmap, uint start, uint lines, byte[] pixels, ref BitmapInformation information, uint usage);

    private delegate bool MonitorCallback(nint monitor, nint context, ref NativeRectangle rectangle, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X; internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        internal int Left; internal int Top; internal int Right; internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInformation
    {
        internal int Size; internal int Flags; internal nint Cursor; internal NativePoint Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInformation
    {
        internal int IsIcon; internal int HotspotX; internal int HotspotY; internal nint Mask; internal nint Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        internal int Type; internal int Width; internal int Height; internal int WidthBytes; internal ushort Planes; internal ushort Bits; internal nint Pixels;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInformation
    {
        internal uint Size; internal int Width; internal int Height; internal ushort Planes; internal ushort Bits;
        internal uint Compression; internal uint ImageSize; internal int XResolution; internal int YResolution;
        internal uint ColorsUsed; internal uint ColorsImportant; internal uint Black; internal uint White;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInformation
    {
        internal int Size; internal NativeRectangle Monitor; internal NativeRectangle Work; internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Name;
    }
}
