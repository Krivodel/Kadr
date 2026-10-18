using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Kadr.Desktop.Services.Export;

public sealed class WindowsClipboardWriter : IClipboardWriter
{
    private readonly ILogger<WindowsClipboardWriter> _logger;

    public WindowsClipboardWriter(ILogger<WindowsClipboardWriter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task WriteImageAsync(SKBitmap image, ScreenshotFormat format, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(image);
        byte[] encoded = await Task.Run(() => ImageEncoder.Encode(image, format), ct).ConfigureAwait(false);
        if (format == ScreenshotFormat.Jpeg)
        {
            string path = await ClipboardImageFile.CreateAsync(encoded, ct).ConfigureAwait(false);
            bool copied = false;
            try
            {
                await WriteAsync(new Dictionary<uint, byte[]>
                {
                    [15] = ClipboardImageFile.CreateDropList(path),
                    [RegisterClipboardFormatW("JFIF")] = encoded,
                    [RegisterClipboardFormatW("image/jpeg")] = encoded
                }, ct).ConfigureAwait(false);
                copied = true;
            }
            finally
            {
                ClipboardImageFile.Finish(path, copied, _logger);
            }
            return;
        }
        byte[] dib = await Task.Run(() => CreateDib(image), ct).ConfigureAwait(false);
        await WriteAsync(new Dictionary<uint, byte[]>
        {
            [RegisterClipboardFormatW("PNG")] = encoded,
            [RegisterClipboardFormatW("image/png")] = encoded,
            [17] = dib
        }, ct).ConfigureAwait(false);
        ClipboardImageFile.RemoveObsolete(null, _logger);
    }

    public async Task WriteTextAsync(string text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        await WriteAsync(new Dictionary<uint, byte[]> { [13] = Encoding.Unicode.GetBytes(text + '\0') }, ct).ConfigureAwait(false);
        ClipboardImageFile.RemoveObsolete(null, _logger);
    }

    private static byte[] CreateDib(SKBitmap image)
    {
        const int HeaderSize = 124;
        byte[] data = new byte[checked(HeaderSize + (image.Width * image.Height * 4))];
        using MemoryStream stream = new(data);
        using BinaryWriter writer = new(stream);
        writer.Write(HeaderSize);
        writer.Write(image.Width);
        writer.Write(-image.Height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(3);
        writer.Write(image.Width * image.Height * 4);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0x00ff0000u);
        writer.Write(0x0000ff00u);
        writer.Write(0x000000ffu);
        writer.Write(0xff000000u);
        writer.Write(0x73524742u);
        using SKBitmap straight = new(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using SKPixmap pixels = image.PeekPixels();
        pixels.ReadPixels(straight.Info, straight.GetPixels(), straight.RowBytes);
        Marshal.Copy(straight.GetPixels(), data, HeaderSize, image.Width * image.Height * 4);
        return data;
    }

    private static async Task WriteAsync(IReadOnlyDictionary<uint, byte[]> formats, CancellationToken ct)
    {
        Dictionary<uint, nint> handles = [];

        try
        {
            foreach (KeyValuePair<uint, byte[]> format in formats)
            {
                nint handle = GlobalAlloc(0x0002, (nuint)format.Value.Length);

                if (handle == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                handles.Add(format.Key, handle);
                nint pointer = GlobalLock(handle);

                if (pointer == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                Marshal.Copy(format.Value, 0, pointer, format.Value.Length);
                GlobalUnlock(handle);
            }

            int attempt = 0;
            nint owner;

            while (!TryOpen(out owner))
            {
                if (++attempt >= 8)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The clipboard is busy.");
                }

                await Task.Delay(attempt * 10, ct).ConfigureAwait(false);
            }

            try
            {
                if (!EmptyClipboard())
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                foreach (uint format in handles.Keys.ToArray())
                {
                    if (SetClipboardData(format, handles[format]) == 0)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }

                    handles[format] = 0;
                }
            }
            finally
            {
                CloseClipboard();
                DestroyWindow(owner);
            }
        }
        finally
        {
            foreach (nint handle in handles.Values.Where(handle => handle != 0))
            {
                GlobalFree(handle);
            }
        }
    }

    private static bool TryOpen(out nint owner)
    {
        owner = CreateWindowExW(0, "STATIC", "Kadr clipboard", 0, 0, 0, 0, 0, new nint(-3), 0, 0, 0);

        if (owner == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (OpenClipboard(owner))
        {
            return true;
        }

        DestroyWindow(owner);
        return false;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetClipboardData(uint format, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalLock(nint handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint handle);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint extendedStyle, string className, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
}
