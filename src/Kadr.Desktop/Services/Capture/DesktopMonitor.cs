using Kadr.Desktop.Services.Geometry;

namespace Kadr.Desktop.Services.Capture;

public sealed record DesktopMonitor(nint Handle, PixelRectangle Bounds, string Name, bool IsHdr = false);
