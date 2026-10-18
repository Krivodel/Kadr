using System.Runtime.InteropServices;

using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Geometry;
using SkiaSharp;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class TextureReadbackTests
{
    [Fact]
    public void Read_SdrBgraTexture_PreservesEveryChannelWithoutGammaOrResampling()
    {
        CaptureDeviceFactory.Create(null, out ID3D11Device device, out ID3D11DeviceContext context);
        using (device)
        using (context)
        using (ID3D11Texture2D texture = device.CreateTexture2D(new Texture2DDescription
        {
            Width = 16,
            Height = 16,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read | CpuAccessFlags.Write
        }))
        {
            MappedSubresource mapped = context.Map(texture, 0, MapMode.Write, Vortice.Direct3D11.MapFlags.None);

            try
            {
                for (int y = 0; y < 16; y++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        int value = (y * 16) + x;
                        int pixel = value | ((255 - value) << 8) | (((value * 17) % 256) << 16);
                        Marshal.WriteInt32(mapped.DataPointer, (int)(y * mapped.RowPitch) + (x * 4), pixel);
                    }
                }
            }
            finally
            {
                context.Unmap(texture, 0);
            }

            DesktopMonitor monitor = new(1, new PixelRectangle(0, 0, 16, 16), "Test SDR");
            FrozenOutput output = new(texture, context, monitor, 1, 1);

            using SKBitmap result = TextureReadback.Read(output);

            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    int value = (y * 16) + x;
                    Assert.Equal(new SKColor((byte)((value * 17) % 256), (byte)(255 - value), (byte)value, 255), result.GetPixel(x, y));
                }
            }
        }
    }
}
