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
            using UpdateViewModel updates = new(service, ct => Task.FromResult(Guid.NewGuid().ToString("N")),
                () => { }, new UpdateErrorHandler(NullLogger<UpdateErrorHandler>.Instance));
            DrawingDocument document = TestDocuments.Create(1100, 760, SKColor.Parse("#252B3D"));
            document.SetElements(ImmutableArray.Create(
                TestDocuments.Element(DrawingToolId.Arrow, 200, 240, 580, 320) with { Thickness = 12, Color = 0xff409bff },
                TestDocuments.Element(DrawingToolId.Rectangle, 180, 160, 640, 420) with { Color = 0xffa585ff, Thickness = 4 }));
            using EditorViewModel editor = EditorViewModelTests.Create(new EditorTestServices(), document);
            editor.Updates = updates;
            updates.PropertyChanged += (_, arguments) =>
            {
                if (arguments.PropertyName == nameof(UpdateViewModel.IsLoading))
                {
                    editor.IsLoading = updates.IsLoading;
                }
            };
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
                Assert.True(updates.IsOpen);
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
