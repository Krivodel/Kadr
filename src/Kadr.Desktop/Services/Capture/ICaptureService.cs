namespace Kadr.Desktop.Services.Capture;

public interface ICaptureService : IAsyncDisposable
{
    Task ConfigureAsync(CaptureBackend backend, CaptureMode mode, CancellationToken ct);
    Task<CapturedDesktop> CaptureAsync(CancellationToken ct);
}
