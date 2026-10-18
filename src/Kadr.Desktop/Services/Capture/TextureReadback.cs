using System.Runtime.Intrinsics;

using SkiaSharp;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Kadr.Desktop.Services.Capture;

internal static class TextureReadback
{
    private static readonly byte[] HalfToSrgb = Enumerable.Range(0, 65536)
        .Select(value => ToneMap((float)BitConverter.UInt16BitsToHalf((ushort)value))).ToArray();
    private static readonly byte[] HalfToSdr = Enumerable.Range(0, 65536)
        .Select(value => ToSrgb((float)BitConverter.UInt16BitsToHalf((ushort)value))).ToArray();

    internal static unsafe SKBitmap Read(FrozenOutput output)
    {
        Texture2DDescription description = output.Texture.Description;
        int width = (int)description.Width;
        int height = (int)description.Height;
        bool sideways = (output.Rotation == 2) || (output.Rotation == 4);
        SKBitmap bitmap = new(sideways ? height : width, sideways ? width : height, SKColorType.Bgra8888, SKAlphaType.Premul);
        MappedSubresource mapped = default;
        bool isMapped = false;

        try
        {
            mapped = output.Context.Map(output.Texture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            isMapped = true;
            uint* destination = (uint*)bitmap.GetPixels();

            if ((description.Format == Format.B8G8R8A8_UNorm) && (output.Rotation <= 1))
            {
                for (int y = 0; y < height; y++)
                {
                    uint* source = (uint*)((byte*)mapped.DataPointer + (y * mapped.RowPitch));
                    uint* target = destination + (y * width);
                    int x = 0;

                    if (Vector256.IsHardwareAccelerated)
                    {
                        Vector256<uint> alpha = Vector256.Create(0xff000000u);

                        for (; x <= width - Vector256<uint>.Count; x += Vector256<uint>.Count)
                        {
                            (Vector256.Load(source + x) | alpha).Store(target + x);
                        }
                    }

                    for (; x < width; x++)
                    {
                        target[x] = source[x] | 0xff000000;
                    }
                }

                return bitmap;
            }

            if ((description.Format == Format.R16G16B16A16_Float) && (output.Rotation <= 1))
            {
                byte[] conversion = output.Monitor.IsHdr ? HalfToSrgb : HalfToSdr;

                fixed (byte* table = conversion)
                {
                    for (int y = 0; y < height; y++)
                    {
                        ushort* source = (ushort*)((byte*)mapped.DataPointer + (y * mapped.RowPitch));
                        uint* target = destination + (y * width);

                        for (int x = 0; x < width; x++)
                        {
                            target[x] = 0xff000000 | ((uint)table[source[0]] << 16)
                                | ((uint)table[source[1]] << 8) | table[source[2]];
                            source += 4;
                        }
                    }
                }

                return bitmap;
            }

            for (int y = 0; y < height; y++)
            {
                byte* row = (byte*)mapped.DataPointer + (y * mapped.RowPitch);

                for (int x = 0; x < width; x++)
                {
                    int targetX = x;
                    int targetY = y;

                    switch (output.Rotation)
                    {
                        case 2:
                            targetX = height - 1 - y;
                            targetY = x;
                            break;
                        case 3:
                            targetX = width - 1 - x;
                            targetY = height - 1 - y;
                            break;
                        case 4:
                            targetX = y;
                            targetY = width - 1 - x;
                            break;
                    }

                    destination[(targetY * bitmap.Width) + targetX] = Decode(row, x, description.Format, output.Monitor.IsHdr);
                }
            }
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
        finally
        {
            if (isMapped)
            {
                output.Context.Unmap(output.Texture, 0);
            }
        }

        return bitmap;
    }

    private static unsafe uint Decode(byte* row, int x, Format format, bool hdr)
    {
        if (format == Format.B8G8R8A8_UNorm)
        {
            return ((uint*)row)[x] | 0xff000000;
        }

        float red;
        float green;
        float blue;

        if (format == Format.R16G16B16A16_Float)
        {
            ushort* pixel = (ushort*)row + (x * 4);
            byte[] table = hdr ? HalfToSrgb : HalfToSdr;
            return 0xff000000 | ((uint)table[pixel[0]] << 16) | ((uint)table[pixel[1]] << 8) | table[pixel[2]];
        }
        else
        {
            uint pixel = ((uint*)row)[x];

            if (!hdr)
            {
                return 0xff000000 | (((pixel & 1023) * 255 / 1023) << 16)
                    | (((pixel >> 10) & 1023) * 255 / 1023 << 8) | (((pixel >> 20) & 1023) * 255 / 1023);
            }

            red = DecodePq((pixel & 1023) / 1023f);
            green = DecodePq(((pixel >> 10) & 1023) / 1023f);
            blue = DecodePq(((pixel >> 20) & 1023) / 1023f);
            (red, green, blue) = ((1.6605f * red) - (0.5876f * green) - (0.0728f * blue),
                (-0.1246f * red) + (1.1329f * green) - (0.0083f * blue),
                (-0.0182f * red) - (0.1006f * green) + (1.1187f * blue));
        }

        return 0xff000000 | ((uint)ToneMap(red) << 16) | ((uint)ToneMap(green) << 8) | ToneMap(blue);
    }

    private static float DecodePq(float value)
    {
        const float M1 = 2610f / 16384;
        const float M2 = 2523f / 32;
        float power = MathF.Pow(value, 1 / M2);
        return MathF.Pow(MathF.Max(power - (3424f / 4096), 0) / ((2413f / 128) - ((2392f / 128) * power)), 1 / M1) * 125;
    }

    private static byte ToneMap(float value)
    {
        if (!float.IsFinite(value))
        {
            return value > 0 ? (byte)255 : (byte)0;
        }

        float linear = MathF.Max(0, value * (80f / 203));
        float mapped = Math.Clamp((linear * ((2.51f * linear) + 0.03f))
            / ((linear * ((2.43f * linear) + 0.59f)) + 0.14f), 0, 1);
        return ToSrgb(mapped);
    }

    private static byte ToSrgb(float value)
    {
        float mapped = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : value > 0 ? 1 : 0;
        float srgb = mapped <= 0.0031308f ? mapped * 12.92f : (1.055f * MathF.Pow(mapped, 1 / 2.4f)) - 0.055f;
        return (byte)Math.Clamp((int)MathF.Round(srgb * 255), 0, 255);
    }
}
