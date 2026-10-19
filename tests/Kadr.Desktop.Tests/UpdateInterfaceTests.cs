using System.Collections.Immutable;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Updates;
using Kadr.Desktop.ViewModels;
using Kadr.Desktop.Views;
using Krivodeling.Localization.Avalonia;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Kadr.Desktop.Tests;

[Collection("Avalonia UI")]
public sealed class UpdateInterfaceTests
{
    [Fact]
    public async Task Restore_BeforeOpeningEditor_ShowsToolbarsWithoutMovingSelectionAndRepositionsAfterResize()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel original = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1100, 760));
            original.Document.SetSelection(new PixelRectangle(95, 90, 760, 470));
            EditorSessionSnapshot snapshot = EditorSessionSnapshot.Capture(original);
            using EditorViewModel restored = EditorViewModelTests.Create(new EditorTestServices(), new DrawingDocument(snapshot.CreateDesktop(), false));
            snapshot.Restore(restored);
            EditorWindow window = UiTestSession.CreateEditorWindow(restored);
            window.Show();
            try
            {
                await UiTestSession.PumpFramesAsync(220);
                Border actions = window.FindControl<Border>("Actions") ?? throw new InvalidOperationException();
                Border tools = window.FindControl<Border>("ToolsPanel") ?? throw new InvalidOperationException();
                Assert.True(actions.IsHitTestVisible);
                Assert.True(tools.IsHitTestVisible);
                Assert.Equal(1, actions.Opacity);
                Assert.Equal(1, tools.Opacity);
                Assert.True(actions.Bounds.Width > 0 && actions.Bounds.Height > 0);
                Assert.True(tools.Bounds.Width > 0 && tools.Bounds.Height > 0);

                window.Width = 800;
                window.Height = 500;
                await UiTestSession.PumpFramesAsync(220);

                Assert.Equal(snapshot.Selection, restored.Document.Selection);
                Assert.True(Canvas.GetLeft(actions) + actions.Bounds.Width <= window.ClientSize.Width);
                Assert.True(Canvas.GetTop(actions) + actions.Bounds.Height <= window.ClientSize.Height);
                Assert.True(Canvas.GetLeft(tools) + tools.Bounds.Width <= window.ClientSize.Width);
                Assert.True(Canvas.GetTop(tools) + tools.Bounds.Height <= window.ClientSize.Height);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Apply_ActiveStrokeAtRestart_CommitsVisibleStrokeAndUndoHistory()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel editor = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(800, 600));
            editor.Document.SetSelection(editor.Document.Bounds);
            editor.Interaction.Tool = DrawingToolId.Pencil;
            FakeUpdateService service = new() { DownloadCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
            EditorSessionSnapshot? saved = null;
            using UpdateViewModel updates = new(service, ct =>
            {
                saved = EditorSessionSnapshot.Capture(editor);
                return Task.FromResult<string?>("session");
            }, () => { }, new UpdateErrorHandler(NullLogger<UpdateErrorHandler>.Instance));
            editor.Updates = updates;
            updates.SetRelease(await service.CheckAsync(CancellationToken.None));
            Task apply = updates.ApplyCommand.ExecuteAsync(null);
            EditorWindow window = UiTestSession.CreateEditorWindow(editor);
            window.Show();
            try
            {
                await UiTestSession.PumpFramesAsync(80);
                window.MouseDown(new Avalonia.Point(100, 100), MouseButton.Left);
                window.MouseMove(new Avalonia.Point(250, 180));

                service.DownloadCompletion.SetResult();
                await apply;
                EditorSessionSnapshot snapshot = saved ?? throw new InvalidOperationException("The active stroke was not saved.");
                using EditorViewModel restored = EditorViewModelTests.Create(new EditorTestServices(), new DrawingDocument(snapshot.CreateDesktop(), false));
                snapshot.Restore(restored);

                DrawingElement stroke = Assert.Single(restored.Document.Elements);
                Assert.Equal(DrawingToolId.Pencil, stroke.Tool);
                Assert.Equal(new Services.Geometry.PixelPoint(250, 180), stroke.Points[^1]);
                Assert.False(editor.Interaction.IsGestureActive);
                restored.History.Undo();
                Assert.Empty(restored.Document.Elements);
            }
            finally
            {
                updates.Dispose();
                await apply;
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PrepareForUpdate_ActiveTextInput_CommitsTextAndRestoresUndoableObject()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel editor = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(800, 600));
            EditorWindow window = UiTestSession.CreateEditorWindow(editor);
            window.Show();
            try
            {
                editor.Interaction.Tool = DrawingToolId.Text;
                await UiTestSession.PumpFramesAsync(80);
                window.MouseDown(new Avalonia.Point(100, 100), MouseButton.Left);
                window.MouseUp(new Avalonia.Point(100, 100), MouseButton.Left);
                await UiTestSession.PumpFramesAsync(80);
                TextBox input = window.FindControl<TextBox>("TextEntry")!;
                Assert.True(input.IsVisible);
                input.Text = "Draft text before updating";

                window.PrepareForUpdate();
                EditorSessionSnapshot snapshot = EditorSessionSnapshot.Capture(editor);
                using EditorViewModel restored = EditorViewModelTests.Create(new EditorTestServices(), new DrawingDocument(snapshot.CreateDesktop(), false));
                snapshot.Restore(restored);

                Assert.Equal("Draft text before updating", Assert.Single(restored.Document.Elements).Text);
                Assert.False(editor.Interaction.IsTextEditRequested);
                restored.History.Undo();
                Assert.Empty(restored.Document.Elements);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_UpdateAvailable_ShowsNotificationConfirmationAndDownloadProgress()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using LocalizationService localization = new(new DirectoryLocalizationFileStore(Path.Combine(Path.GetTempPath(), "Kadr-test-localizations")),
                Kadr.Desktop.Resources.UiText.Catalog, NullLogger<LocalizationService>.Instance);
            await localization.RefreshAvailableLocalizationsAsync(CancellationToken.None);
            string previousLanguage = localization.CurrentLocalization?.Id ?? LocalizationConstants.EnglishId;
            localization.Select(LocalizationConstants.EnglishId);
            localization.Select(LocalizationConstants.RussianId);
            FakeUpdateService service = new() { DownloadCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
            using UpdateViewModel updates = new(service, ct => Task.FromResult<string?>(Guid.NewGuid().ToString("N")),
                () => { }, new UpdateErrorHandler(NullLogger<UpdateErrorHandler>.Instance));
            DrawingDocument document = TestDocuments.Create(1100, 760, SKColor.Parse("#252B3D"));
            document.SetElements(ImmutableArray.Create(
                TestDocuments.Element(DrawingToolId.Arrow, 200, 240, 580, 320) with { Thickness = 12, Color = 0xff409bff },
                TestDocuments.Element(DrawingToolId.Rectangle, 180, 160, 640, 420) with { Color = 0xffa585ff, Thickness = 4 }));
            using EditorViewModel editor = EditorViewModelTests.Create(new EditorTestServices(), document);
            editor.Updates = updates;
            document.SetSelection(new PixelRectangle(95, 90, 760, 470));
            EditorWindow window = UiTestSession.CreateEditorWindow(editor);
            window.Show();
            try
            {
                await UiTestSession.PumpFramesAsync(80);
                Assert.False(window.FindControl<Button>("UpdateButton")!.IsVisible);
                updates.SetRelease(await service.CheckAsync(CancellationToken.None));
                await UiTestSession.PumpFramesAsync(220);
                Button button = window.FindControl<Button>("UpdateButton")!;
                Assert.True(button.IsVisible);
                UiTestSession.SaveFrame(window, "update-notification.png");

                updates.OpenCommand.Execute(null);
                await UiTestSession.PumpFramesAsync(220);
                UpdatePanel popup = window.FindControl<UpdatePanel>("UpdatePopup")!;
                Assert.True(popup.IsHitTestVisible);
                Assert.Equal("Доступно обновление 1.1.0", updates.Title);
                Assert.Contains(popup.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == updates.Title);
                UiTestSession.SaveFrame(window, "update-confirmation.png");
                using (SKBitmap frame = UiTestSession.CapturePixels(window))
                {
                    Avalonia.Point belowPanel = popup.TranslatePoint(new Avalonia.Point(popup.Bounds.Width / 2, popup.Bounds.Height + 4), window)
                        ?? throw new InvalidOperationException();
                    SKColor shadow = frame.GetPixel((int)belowPanel.X, (int)belowPanel.Y);
                    SKColor background = document.Desktop.Image.GetPixel((int)belowPanel.X, (int)belowPanel.Y);
                    Assert.True(shadow.Red < background.Red && shadow.Green < background.Green && shadow.Blue < background.Blue,
                        "The rounded panel's shadow must extend below its bounds without a rectangular cutoff.");
                }
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await UiTestSession.PumpFramesAsync(180);
                Assert.False(updates.IsOpen);
                Assert.True(window.IsVisible);

                updates.OpenCommand.Execute(null);
                Task apply = updates.ApplyCommand.ExecuteAsync(null);
                await UiTestSession.PumpFramesAsync(80);
                service.Progress!.Report(new UpdateProgress(63));
                await UiTestSession.PumpFramesAsync(220);
                Assert.Equal(63, popup.GetVisualDescendants().OfType<ProgressBar>().Single().Value);
                UiTestSession.SaveFrame(window, "update-progress.png");
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Assert.False(updates.IsOpen);
                Assert.False(editor.IsInputBlocked);
                Assert.True(window.FindControl<Kadr.Desktop.Controls.ScreenshotCanvas>("Surface")!.IsHitTestVisible);
                updates.OpenCommand.Execute(null);
                await UiTestSession.PumpFramesAsync(180);
                Assert.Equal(63, popup.GetVisualDescendants().OfType<ProgressBar>().Single().Value);
                popup.FindControl<Button>("CloseUpdateButton")!.Command!.Execute(null);
                Assert.False(updates.IsOpen);
                updates.Dispose();
                await apply;
            }
            finally
            {
                window.Close();
                localization.Select(previousLanguage);
            }
            return true;
        }, CancellationToken.None);
    }
}
