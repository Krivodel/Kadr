using Kadr.Desktop.Services.Updates;

namespace Kadr.Desktop.Tests;

internal sealed class FakeUpdateService : IUpdateService
{
    internal UpdateRelease? Release { get; set; } = new("1.1.0");
    internal Exception? DownloadError { get; set; }
    internal Exception? InstallError { get; set; }
    internal TaskCompletionSource? DownloadCompletion { get; set; }
    internal IProgress<UpdateProgress>? Progress { get; private set; }
    internal List<string> Operations { get; } = [];

    public Task<UpdateRelease?> CheckAsync(CancellationToken ct)
    {
        return Task.FromResult(Release);
    }

    public async Task DownloadAsync(IProgress<UpdateProgress> progress, CancellationToken ct)
    {
        Operations.Add("download");
        Progress = progress;
        if (DownloadError is not null)
        {
            throw DownloadError;
        }
        if (DownloadCompletion is not null)
        {
            await DownloadCompletion.Task.WaitAsync(ct);
        }
    }

    public Task InstallAsync(string sessionId, CancellationToken ct)
    {
        Operations.Add("install:" + sessionId);
        if (InstallError is not null)
        {
            throw InstallError;
        }
        return Task.CompletedTask;
    }
}
