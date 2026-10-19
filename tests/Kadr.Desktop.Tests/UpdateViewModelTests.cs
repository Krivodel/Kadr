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
        using UpdateViewModel model = new(service, ct => { saves++; return Task.FromResult<string?>("session"); },
            () => { restarted = true; }, new UpdateErrorHandler(NullLogger<UpdateErrorHandler>.Instance), id => { discarded = id; });
        using EditorViewModel editor = EditorViewModelTests.Create(new EditorTestServices());
        editor.Updates = model;
        model.SetRelease(await service.CheckAsync(CancellationToken.None));
        model.OpenCommand.Execute(null);

        await model.ApplyCommand.ExecuteAsync(null);

        Assert.False(restarted);
        Assert.Equal(failInstall ? 1 : 0, saves);
        Assert.Equal(failInstall ? "session" : null, discarded);
        Assert.True(model.IsOpen);
        Assert.True(model.HasErrorMessage);
        Assert.False(model.IsLoading);
        Assert.False(editor.IsInputBlocked);
        Assert.True(model.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Apply_Success_SavesSessionBeforeInstallationAndRestartsOnce()
    {
        FakeUpdateService service = new();
        using UpdateViewModel model = new(service,
            ct => { service.Operations.Add("save"); return Task.FromResult<string?>("session"); },
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
        using UpdateViewModel model = new(service, ct => Task.FromResult<string?>("session"),
            () => { }, new UpdateErrorHandler(NullLogger<UpdateErrorHandler>.Instance));
        model.SetRelease(await service.CheckAsync(CancellationToken.None));
        model.OpenCommand.Execute(null);

        model.DismissCommand.Execute(null);

        Assert.False(model.IsOpen);
        Assert.True(model.IsAvailable);
    }

    [Fact]
    public async Task Apply_DownloadInProgress_KeepsEditorUsableAndCapturesLatestStateBeforeRestart()
    {
        FakeUpdateService service = new()
        {
            DownloadCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            InstallCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        using EditorViewModel editor = EditorViewModelTests.Create(new EditorTestServices());
        TaskCompletionSource<EditorSessionSnapshot> saved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using UpdateViewModel model = new(service, ct =>
        {
            Assert.True(editor.IsInputBlocked);
            Assert.False(editor.Updates?.DismissCommand.CanExecute(null));
            saved.SetResult(EditorSessionSnapshot.Capture(editor));
            return Task.FromResult<string?>("session");
        }, () => { }, new UpdateErrorHandler(NullLogger<UpdateErrorHandler>.Instance));
        editor.Updates = model;
        model.SetRelease(await service.CheckAsync(CancellationToken.None));
        model.OpenCommand.Execute(null);

        Task apply = model.ApplyCommand.ExecuteAsync(null);
        try
        {
            Assert.False(editor.IsInputBlocked);
            Assert.False(saved.Task.IsCompleted);
            model.DismissCommand.Execute(null);
            Assert.False(model.IsOpen);
            model.OpenCommand.Execute(null);
            Assert.True(model.IsOpen);
            editor.Interaction.Tool = Kadr.Desktop.Services.Editing.DrawingToolId.Arrow;
            editor.Interaction.Begin(new Services.Geometry.PixelPoint(30, 30), 5);
            editor.Interaction.Complete(new Services.Geometry.PixelPoint(90, 70), false);
            Assert.Single(editor.Document.Elements);

            service.DownloadCompletion.SetResult();
            EditorSessionSnapshot snapshot = await saved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using EditorViewModel restored = EditorViewModelTests.Create(new EditorTestServices(),
                new Services.Editing.DrawingDocument(snapshot.CreateDesktop(), false));
            snapshot.Restore(restored);
            Assert.Equal(editor.Document.Elements.ToArray(), restored.Document.Elements.ToArray());
            int closes = 0;
            editor.CloseRequested += (_, _) => closes++;
            editor.CancelCommand.Execute(null);
            Assert.Equal(0, closes);
            Assert.False(model.DismissCommand.CanExecute(null));
        }
        finally
        {
            service.InstallCompletion.TrySetResult();
            await apply;
        }

    }

    [Fact]
    public async Task Apply_EditorFinishingOperation_WaitsBeforeBlockingInputAndSaving()
    {
        FakeUpdateService service = new();
        TaskCompletionSource idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool saved = false;
        using UpdateViewModel model = new(service, ct => { saved = true; return Task.FromResult<string?>(null); },
            () => { }, new UpdateErrorHandler(NullLogger<UpdateErrorHandler>.Instance), waitForEditor: ct => idle.Task.WaitAsync(ct));
        model.SetRelease(await service.CheckAsync(CancellationToken.None));

        Task apply = model.ApplyCommand.ExecuteAsync(null);
        Assert.False(model.IsPreparingRestart);
        Assert.False(saved);
        idle.SetResult();
        await apply;

        Assert.True(saved);
        Assert.Equal(new string[] { "download", "install:" }, service.Operations);
        Assert.False(model.HasErrorMessage);
    }

    [Fact]
    public async Task Apply_DisposedDuringDownload_DoesNotSaveInstallOrRestart()
    {
        FakeUpdateService service = new() { DownloadCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        bool saved = false;
        bool restarted = false;
        using UpdateViewModel model = new(service, ct => { saved = true; return Task.FromResult<string?>(null); },
            () => { restarted = true; }, new UpdateErrorHandler(NullLogger<UpdateErrorHandler>.Instance));
        model.SetRelease(await service.CheckAsync(CancellationToken.None));

        Task apply = model.ApplyCommand.ExecuteAsync(null);
        model.DismissCommand.Execute(null);
        model.Dispose();
        await apply;

        Assert.False(saved);
        Assert.False(restarted);
        Assert.Equal(new string[] { "download" }, service.Operations);
        Assert.False(model.HasErrorMessage);
    }

    [Fact]
    public async Task Apply_ScreenshotCancelledDuringDownload_RestartsWithoutSavingClosedEditor()
    {
        FakeUpdateService service = new() { DownloadCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        using EditorViewModel editor = EditorViewModelTests.Create(new EditorTestServices());
        bool closed = false;
        bool restarted = false;
        editor.CloseRequested += (_, _) => closed = true;
        using UpdateViewModel model = new(service, ct =>
        {
            Assert.True(closed);
            return Task.FromResult<string?>(null);
        }, () => restarted = true, new UpdateErrorHandler(NullLogger<UpdateErrorHandler>.Instance));
        editor.Updates = model;
        model.SetRelease(await service.CheckAsync(CancellationToken.None));

        Task apply = model.ApplyCommand.ExecuteAsync(null);
        editor.CancelCommand.Execute(null);
        Assert.True(closed);
        Assert.True(model.IsLoading);
        service.DownloadCompletion.SetResult();
        await apply;

        Assert.Equal(new string[] { "download", "install:" }, service.Operations);
        Assert.True(restarted);
        Assert.False(model.HasErrorMessage);
    }
}
