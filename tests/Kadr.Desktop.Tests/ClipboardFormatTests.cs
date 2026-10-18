using System.Runtime.InteropServices;
using System.Text;

using Kadr.Desktop.Services.Export;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class ClipboardFormatTests
{
    [Fact]
    public async Task Copy_Jpeg_IsFileWithExactEncodedBytesAndPngReplacesIt()
    {
        using SKBitmap image = new(20, 20);
        image.Erase(SKColors.Coral);
        byte[] expected = ImageEncoder.Encode(image, ScreenshotFormat.Jpeg);
        WindowsClipboardWriter writer = new(NullLogger<WindowsClipboardWriter>.Instance);

        await writer.WriteImageAsync(image, ScreenshotFormat.Jpeg, CancellationToken.None);

        Assert.True(OpenClipboard(0));
        string path;
        try
        {
            Assert.True(IsClipboardFormatAvailable(15));
            Assert.False(IsClipboardFormatAvailable(17));
            Assert.False(IsClipboardFormatAvailable(8));
            Assert.False(IsClipboardFormatAvailable(RegisterClipboardFormatW("PNG")));
            StringBuilder name = new(1024);
            Assert.NotEqual(0u, DragQueryFileW(GetClipboardData(15), 0, name, (uint)name.Capacity));
            path = name.ToString();
        }
        finally
        {
            CloseClipboard();
        }
        Assert.Equal(".jpg", Path.GetExtension(path));
        Assert.Equal(expected, await File.ReadAllBytesAsync(path));
        await writer.WriteImageAsync(image, ScreenshotFormat.Png, CancellationToken.None);
        Assert.True(IsClipboardFormatAvailable(RegisterClipboardFormatW("PNG")));
        Assert.True(IsClipboardFormatAvailable(17));
        Assert.False(IsClipboardFormatAvailable(15));
        Assert.False(File.Exists(path));
    }

    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFileW(nint drop, uint index, StringBuilder path, uint size);
}
