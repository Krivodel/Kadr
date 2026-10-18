namespace Kadr.Desktop.Services.Capture;

internal interface IMonitorCapture : IDisposable
{
    DesktopMonitor Monitor
    {
        get;
    }
    bool HasFrame
    {
        get;
    }
    bool IsReadyForIdle => HasFrame;
    CapturedCursor? Cursor
    {
        get;
    }
    bool CanToggleCursor
    {
        get;
    }
    void Poll();
    void PrepareCapture()
    {
    }

    void RequestFrame(int timeoutMilliseconds)
    {
        Poll();
    }

    FrozenOutput Freeze();
}
