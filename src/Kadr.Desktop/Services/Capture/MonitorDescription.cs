using Kadr.Desktop.Services.Geometry;
using Vortice.DXGI;

namespace Kadr.Desktop.Services.Capture;

internal static class MonitorDescription
{
    internal static DesktopMonitor Read(IDXGIOutput output)
    {
        OutputDescription description = output.Description;
        using IDXGIOutput6? output6 = output.QueryInterfaceOrNull<IDXGIOutput6>();
        return new DesktopMonitor(description.Monitor,
            new PixelRectangle(description.DesktopCoordinates.Left, description.DesktopCoordinates.Top,
                description.DesktopCoordinates.Right - description.DesktopCoordinates.Left,
                description.DesktopCoordinates.Bottom - description.DesktopCoordinates.Top), description.DeviceName,
            output6 is not null && output6.Description1.ColorSpace == ColorSpaceType.RgbFullG2084NoneP2020);
    }
}
