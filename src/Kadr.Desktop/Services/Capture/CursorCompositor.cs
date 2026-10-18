using SkiaSharp;

namespace Kadr.Desktop.Services.Capture;

public static class CursorCompositor
{
    public static unsafe void Draw(SKBitmap image, CapturedCursor cursor, int originX, int originY)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(cursor);
        byte* destination = (byte*)image.GetPixels();
        int left = (int)cursor.Position.X - originX;
        int top = (int)cursor.Position.Y - originY;

        for (int y = 0; y < cursor.Height; y++)
        {
            for (int x = 0; x < cursor.Width; x++)
            {
                int targetX = left + x;
                int targetY = top + y;

                if ((targetX < 0) || (targetY < 0) || (targetX >= image.Width) || (targetY >= image.Height))
                {
                    continue;
                }

                byte* target = destination + (targetY * image.RowBytes) + (targetX * 4);

                if (cursor.Kind == CursorShapeKind.Monochrome)
                {
                    int mask = 0x80 >> (x % 8);
                    bool and = (cursor.Pixels[(y * cursor.Pitch) + (x / 8)] & mask) != 0;
                    bool xor = (cursor.Pixels[((y + cursor.Height) * cursor.Pitch) + (x / 8)] & mask) != 0;

                    for (int channel = 0; channel < 3; channel++)
                    {
                        target[channel] = (byte)((target[channel] & (and ? 255 : 0)) ^ (xor ? 255 : 0));
                    }
                }
                else
                {
                    int source = (y * cursor.Pitch) + (x * 4);
                    int alpha = cursor.Pixels[source + 3];

                    for (int channel = 0; channel < 3; channel++)
                    {
                        byte value = cursor.Pixels[source + channel];
                        target[channel] = cursor.Kind == CursorShapeKind.MaskedColor
                            ? (alpha == 255 ? (byte)(target[channel] ^ value) : value)
                            : (byte)(((value * alpha) + (target[channel] * (255 - alpha)) + 127) / 255);
                    }
                }

                target[3] = 255;
            }
        }
    }
}
