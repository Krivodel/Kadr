using Kadr.Desktop.Services.Updates;
using Kadr.Desktop.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class UpdateViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apply_DownloadOrInstallFails_LeavesEditorOpenAndAllowsRetry(bool failInstall)
    {
        FakeUpdateService service = new()
        {
            DownloadError = failInstall ? null : new IOException(),
            InstallError = failInstall ? new IOException() : null
        };
        int saves = 0;
        bool restarted = false;
        string? discarded = null;
        using UpdateViewModel model = new(service, ct => { saves++; return Task.FromResult("session"); },
            () => { restarted = true; }, new UpdateErrorHandler(NullLogger<UpdateErrorHandler>.Instance), id => { discarded = id; });
        model.SetRelease(await service.CheckAsync(CancellationToken.None));
        model.OpenCommand.Execute(null);

        await model.ApplyCommand.ExecuteAsync(null);

        Assert.False(restarted);
        Assert.Equal(failInstall ? 1 : 0, saves);
        Assert.Equal(failInstall ? "session" : null, discarded);
        Assert.True(model.IsOpen);
        Assert.True(model.HasErrorMessage);
        Assert.False(model.IsLoading);
        Assert.True(model.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Apply_Success_SavesSessionBeforeInstallationAndRestartsOnce()
    {
        FakeUpdateService service = new();
        using UpdateViewModel model = new(service,
            ct => { service.Operations.Add("save"); return Task.FromResult("session"); },
            () => { service.Operations.Add("restart"); }, new UpdateErrorHandler(NullLogger<UpdateErrorHandler>.Instance));
        model.SetRelease(await service.CheckAsync(CancellationToken.None));

        await model.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(new string[] { "download", "save", "install:session", "restart" }, service.Operations);
        Assert.False(model.HasErrorMessage);
    }

    [Fact]
    public async Task Apply_SnapshotFails_DoesNotInstallOrRestart()
    {
        FakeUpdateService service = new();
        bool restarted = false;
        using UpdateViewModel model = new(service, ct => throw new IOException(),
            () => { restarted = true; }, new UpdateErrorHandler(NullLogger<UpdateErrorHandler>.Instance));
        model.SetRelease(await service.CheckAsync(CancellationToken.None));

        await model.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(new string[] { "download" }, service.Operations);
        Assert.False(restarted);
        Assert.True(model.HasErrorMessage);
    }

    [Fact]
    public async Task Dismiss_AvailableUpdate_HidesPanelAndKeepsNotification()
    {
        FakeUpdateService service = new();
        using UpdateViewModel model = new(service, ct => Task.FromResult("session"),
            () => { }, new UpdateErrorHandler(NullLogger<UpdateErrorHandler>.Instance));
        model.SetRelease(await service.CheckAsync(CancellationToken.None));
        model.OpenCommand.Execute(null);

        model.DismissCommand.Execute(null);

        Assert.False(model.IsOpen);
        Assert.True(model.IsAvailable);
    }
}
