using Kadr.Desktop.Services.Geometry;

namespace Kadr.Desktop.Services.Capture;

public sealed record CapturedCursor(CursorShapeKind Kind, PixelPoint Position, int Width, int Height,
    int Pitch, byte[] Pixels, long UpdatedAt);
