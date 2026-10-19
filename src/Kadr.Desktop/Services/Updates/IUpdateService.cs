namespace Kadr.Desktop.Services.Updates;

public interface IUpdateService
{
    Task<UpdateRelease?> CheckAsync(CancellationToken ct);
    Task DownloadAsync(IProgress<UpdateProgress> progress, CancellationToken ct);
    Task InstallAsync(string? sessionId, CancellationToken ct);
}
