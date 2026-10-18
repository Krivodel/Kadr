using Kadr.Desktop.Services.Geometry;

namespace Kadr.Desktop.Services.Updates;

public sealed record SessionMonitor(PixelRectangle Bounds, string Name, bool IsHdr);
