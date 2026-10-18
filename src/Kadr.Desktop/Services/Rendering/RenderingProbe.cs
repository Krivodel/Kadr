using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Diagnostics;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using SkiaSharp;

namespace Kadr.Desktop.Services.Rendering;

internal static class RenderingProbe
{
    internal static int Run(string outputPath)
    {
        using NativeProbeWindow window = NativeProbeWindow.Create(new PixelRectangle(0, 0, 256, 160), false);
        nint deviceContext = window.DeviceContext;
        nint renderingContext = 0;

        try
        {
            PixelFormatDescriptor descriptor = new()
            {
                Size = (ushort)Marshal.SizeOf<PixelFormatDescriptor>(),
                Version = 1,
                Flags = 0x24,
                ColorBits = 32,
                AlphaBits = 8
            };
            int format = ChoosePixelFormat(deviceContext, ref descriptor);

            if ((format == 0) || !SetPixelFormat(deviceContext, format, ref descriptor))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The offscreen OpenGL test surface could not be initialized.");
            }

            renderingContext = wglCreateContext(deviceContext);

            if ((renderingContext == 0) || !wglMakeCurrent(deviceContext, renderingContext))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The GPU rendering test context could not be activated.");
            }

            using GRGlInterface graphicsInterface = GRGlInterface.Create();
            using GRContext graphics = GRContext.CreateGl(graphicsInterface);
            using DrawingDocument document = CreateDocument();
            document.SetSelection(document.Bounds);
            using DocumentRenderer renderer = new(document, new DrawingToolRegistry());
            using SKSurface partial = SKSurface.Create(graphics, true, document.Desktop.Image.Info);
            using SKSurface full = SKSurface.Create(graphics, true, document.Desktop.Image.Info);
            DrawingElement blur = new(Guid.NewGuid(), DrawingToolId.Blur,
                ImmutableList.Create(new PixelPoint(20, 20), new PixelPoint(3800, 2100)), DrawingDefaults.Color, 8, BlurStrength: DrawingDefaults.MaximumBlurStrength);
            document.SetElements(ImmutableArray.Create(blur));
            using (SKPicture first = renderer.CreateScene())
            {
                partial.Canvas.DrawPicture(first);
            }

            List<object> results = [];
            int worstDifference = 0;
            int worstCpuDifference = 0;

            for (int index = 1; index <= 12; index++)
            {
                DrawingElement changed = blur.Resize(new PixelRectangle(20 + (index * 3), 20, 3780 + index, 2080));
                document.SetElements(ImmutableArray.Create(changed));
                using SKPicture scene = renderer.CreateScene();
                full.Canvas.Clear(SKColors.Transparent);
                full.Canvas.DrawPicture(scene);
                partial.Canvas.Save();
                partial.Canvas.ClipRect(new SKRect(15, 15, 3900, 2120));
                partial.Canvas.Clear(SKColors.Transparent);
                partial.Canvas.DrawPicture(scene);
                partial.Canvas.Restore();
                using SKImage partialImage = partial.Snapshot();
                using SKImage fullImage = full.Snapshot();
                using SKBitmap partialPixels = SKBitmap.FromImage(partialImage);
                using SKBitmap fullPixels = SKBitmap.FromImage(fullImage);
                int difference = Compare(partialPixels, fullPixels);
                using SKBitmap expected = renderer.Export();
                int cpuDifference = Compare(fullPixels, expected);
                worstDifference = Math.Max(worstDifference, difference);
                worstCpuDifference = Math.Max(worstCpuDifference, cpuDifference);
                results.Add(new
                {
                    Frame = index,
                    MaximumChannelDifference = difference,
                    MaximumCpuDifference = cpuDifference
                });
            }

            File.WriteAllText(outputPath, JsonSerializer.Serialize(new
            {
                GpuBackend = "OpenGL",
                Size = "4096x2160",
                Frames = results,
                MaximumChannelDifference = worstDifference,
                MaximumCpuDifference = worstCpuDifference,
                Passed = (worstDifference <= 1) && (worstCpuDifference <= 2)
            }, new JsonSerializerOptions { WriteIndented = true }));
            return (worstDifference <= 1) && (worstCpuDifference <= 2) ? 0 : 1;
        }
        finally
        {
            wglMakeCurrent(0, 0);

            if (renderingContext != 0)
            {
                wglDeleteContext(renderingContext);
            }


        }
    }

    private static unsafe int Compare(SKBitmap first, SKBitmap second)
    {
        ReadOnlySpan<byte> left = new((void*)first.GetPixels(), first.ByteCount);
        ReadOnlySpan<byte> right = new((void*)second.GetPixels(), second.ByteCount);
        if (left.SequenceEqual(right))
        {
            return 0;
        }
        int maximum = 0;
        for (int index = 0; index < left.Length; index++)
        {
            maximum = Math.Max(maximum, Math.Abs(left[index] - right[index]));
        }
        return maximum;
    }

    private static unsafe DrawingDocument CreateDocument()
    {
        SKBitmap image = new(4096, 2160, SKColorType.Bgra8888, SKAlphaType.Premul);

        uint* pixels = (uint*)image.GetPixels();
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                pixels[(y * image.Width) + x] = (uint)new SKColor((byte)(x % 32 * 8), (byte)(y % 32 * 8), (byte)((x + y) % 32 * 8));
            }
        }

        DesktopMonitor monitor = new(1, new PixelRectangle(0, 0, image.Width, image.Height), "GPU pattern");
        return new DrawingDocument(new CapturedDesktop(image, new DesktopMonitor[] { monitor }, monitor.Bounds, null, true,
            new CaptureMetrics(0, 0, 0, Array.Empty<long>())), false);
    }

    [DllImport("gdi32.dll", SetLastError = true)] private static extern int ChoosePixelFormat(nint context, ref PixelFormatDescriptor descriptor);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool SetPixelFormat(nint context, int format, ref PixelFormatDescriptor descriptor);
    [DllImport("opengl32.dll", SetLastError = true)] private static extern nint wglCreateContext(nint context);
    [DllImport("opengl32.dll", SetLastError = true)] private static extern bool wglMakeCurrent(nint deviceContext, nint renderingContext);
    [DllImport("opengl32.dll")] private static extern bool wglDeleteContext(nint context);

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormatDescriptor
    {
        internal ushort Size;
        internal ushort Version;
        internal uint Flags;
        internal byte PixelType;
        internal byte ColorBits;
        internal byte RedBits;
        internal byte RedShift;
        internal byte GreenBits;
        internal byte GreenShift;
        internal byte BlueBits;
        internal byte BlueShift;
        internal byte AlphaBits;
        internal byte AlphaShift;
        internal byte AccumBits;
        internal byte AccumRedBits;
        internal byte AccumGreenBits;
        internal byte AccumBlueBits;
        internal byte AccumAlphaBits;
        internal byte DepthBits;
        internal byte StencilBits;
        internal byte AuxiliaryBuffers;
        internal byte LayerType;
        internal byte Reserved;
        internal uint LayerMask;
        internal uint VisibleMask;
        internal uint DamageMask;
    }
}
