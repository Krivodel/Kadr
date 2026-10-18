using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Kadr.Desktop.Services.Capture;

internal static class CaptureDeviceFactory
{
    private static readonly FeatureLevel[] Levels = new FeatureLevel[]
    {
        FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0,
        FeatureLevel.Level_9_3, FeatureLevel.Level_9_2, FeatureLevel.Level_9_1
    };

    internal static void Create(IDXGIAdapter? adapter, out ID3D11Device device, out ID3D11DeviceContext context)
    {
        D3D11.D3D11CreateDevice(adapter, adapter is null ? DriverType.Hardware : DriverType.Unknown,
            DeviceCreationFlags.BgraSupport, Levels, out device, out context).CheckError();
    }
}
