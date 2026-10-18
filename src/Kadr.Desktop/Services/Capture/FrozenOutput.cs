using Vortice.Direct3D11;

namespace Kadr.Desktop.Services.Capture;

internal sealed class FrozenOutput : IDisposable
{
    internal ID3D11Texture2D Texture
    {
        get;
    }
    internal ID3D11DeviceContext Context
    {
        get;
    }
    internal DesktopMonitor Monitor
    {
        get;
    }
    internal int Rotation
    {
        get;
    }
    internal long PresentedAt
    {
        get;
    }

    internal FrozenOutput(ID3D11Texture2D texture, ID3D11DeviceContext context,
        DesktopMonitor monitor, int rotation, long presentedAt)
    {
        Texture = texture;
        Context = context;
        Monitor = monitor;
        Rotation = rotation;
        PresentedAt = presentedAt;
    }

    public void Dispose()
    {
        Texture.Dispose();
    }
}
