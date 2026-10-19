using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;

using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.ViewModels;
using Kadr.Desktop.Views;
using Xunit;

namespace Kadr.Desktop.Tests;

[Collection("Avalonia UI")]
public sealed class EditorOcrCancellationTests
{
    [Fact]
    public async Task RecognizeAsync_EscapeDuringRecognition_PreservesScreenshotAndClipboard()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            EditorTestServices services = new() { OcrCompletion = new TaskCompletionSource<string>() };
            using EditorViewModel model = EditorViewModelTests.Create(services);
            PixelRectangle selection = model.Document.Selection;
            EditorWindow window = UiTestSession.CreateEditorWindow(model);

            try
            {
                window.Show();
                await UiTestSession.PumpFramesAsync(80);
                Task recognition = model.RecognizeCommand.ExecuteAsync(null);
                Assert.True(model.IsLoading);

                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await recognition;

                Assert.False(model.IsLoading);
                Assert.True(window.IsVisible);
                Assert.Equal(selection, model.Document.Selection);
                Assert.Equal(0, services.ClipboardWrites);
                Assert.Null(model.ErrorMessage);
            }
            finally
            {
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }
}
