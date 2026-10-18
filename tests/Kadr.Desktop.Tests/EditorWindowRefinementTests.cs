using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

using Kadr.Desktop.Controls;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Export;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Rendering;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.ViewModels;
using Kadr.Desktop.Views;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;
using PixelPoint = Kadr.Desktop.Services.Geometry.PixelPoint;

namespace Kadr.Desktop.Tests;

[Collection("Avalonia UI")]
public sealed class EditorWindowRefinementTests
{
    [Fact]
    public async Task Shortcuts_CustomControlShiftA_SelectsToolWithoutTriggeringSelectAll()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "Kadr-shortcut-ui-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "settings.json");
            SettingsStore store = new(NullLogger<SettingsStore>.Instance, path);

            try
            {
                await store.SaveAsync(new AppSettings
                {
                    EditorShortcuts = new Dictionary<EditorAction, ShortcutGesture>
                    {
                        [EditorAction.Arrow] = new ShortcutGesture(0x41, ShortcutModifiers.Control | ShortcutModifiers.Shift)
                    }
                }, CancellationToken.None);
                using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800), store);
                PixelRectangle selection = new(300, 200, 600, 350);
                model.Document.SetSelection(selection);
                EditorWindow window = new(model, model.Document.Desktop.Monitors[0], store);
                window.Show();
                await UiTestSession.PumpFramesAsync(140);

                window.KeyPress(Key.A, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.A, "a");

                Assert.Equal(DrawingToolId.Arrow, model.Interaction.Tool);
                Assert.Equal(selection, model.Document.Selection);
                window.Close();
            }
            finally
            {
                File.Delete(path);
                Directory.Delete(directory);
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Thickness_DraggingHorizontally_UsesCenteredAnimatedLineAndPrecisionModifier()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(new PixelRectangle(300, 200, 600, 350));
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(180);
            Button? button = window.FindControl<Button>("ThicknessButton");
            Border? preview = window.FindControl<Border>("ThicknessPreview");
            Assert.NotNull(button);
            Assert.NotNull(preview);
            Point center = button.TranslatePoint(new Point(19, 19), window) ?? throw new InvalidOperationException();
            IReadOnlyList<double> showing = await UiTestSession.ObserveTransitionAsync(preview, Visual.OpacityProperty, 1,
                () => window.MouseDown(center, MouseButton.Left));

            Assert.Contains(showing, value => (value > 0) && (value < 1));
            Assert.Null(preview.Background);
            Assert.Equal(center.X, Canvas.GetLeft(preview) + (preview.Width / 2), 1);
            double initial = model.Interaction.Thickness;
            window.MouseMove(center + new Vector(80, 0));
            Assert.Equal(initial + 10, model.Interaction.Thickness);
            window.MouseMove(center + new Vector(80, 80));
            Assert.Equal(initial + 10, model.Interaction.Thickness);
            window.MouseMove(center + new Vector(104, 80), RawInputModifiers.Shift);
            Assert.Equal(initial + 11, model.Interaction.Thickness);
            UiTestSession.SaveFrame(window, "thickness-preview-above-v2.png");
            IReadOnlyList<double> hiding = await UiTestSession.ObserveTransitionAsync(preview, Visual.OpacityProperty, 0,
                () => window.MouseUp(center + new Vector(104, 80), MouseButton.Left));
            Assert.Contains(hiding, value => (value > 0) && (value < 1));
            Assert.False(model.IsAdjustingThickness);
            model.Document.SetSelection(new PixelRectangle(300, 10, 600, 25));
            await UiTestSession.PumpFramesAsync(140);
            center = button.TranslatePoint(new Point(19, 19), window) ?? throw new InvalidOperationException();
            window.MouseDown(center, MouseButton.Left);
            Assert.True(Canvas.GetTop(preview) > center.Y);
            await UiTestSession.WaitForAsync(() => preview.Opacity == 1);
            UiTestSession.SaveFrame(window, "thickness-preview-below-v2.png");
            window.MouseUp(center, MouseButton.Left);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Shortcuts_ToolAndOcrDefaults_ExecuteCommandsWithoutInterferingWithText()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            EditorTestServices services = new();
            using EditorViewModel model = EditorViewModelTests.Create(services, TestDocuments.Create(1280, 800));
            model.Document.SetSelection(new PixelRectangle(300, 200, 600, 350));
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(160);

            window.KeyPress(Key.D1, RawInputModifiers.None, PhysicalKey.Digit1, "1");
            Assert.Equal(DrawingToolId.Arrow, model.Interaction.Tool);
            window.KeyPress(Key.B, RawInputModifiers.None, PhysicalKey.B, "b");
            Assert.Equal(DrawingToolId.Blur, model.Interaction.Tool);
            window.KeyPress(Key.T, RawInputModifiers.Control, PhysicalKey.T, "t");
            await UiTestSession.WaitForAsync(() => !model.IsLoading);
            Assert.NotNull(model.ErrorMessage);
            Assert.Equal(0, services.ClipboardWrites);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ColorPopup_ClickAndEscape_AnimatesBothDirectionsAndKeepsToolSelected()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(new PixelRectangle(300, 200, 600, 350));
            model.Interaction.Tool = DrawingToolId.Arrow;
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(180);
            Button? button = window.FindControl<Button>("ColorButton");
            Border? palette = window.FindControl<Border>("Palette");
            Assert.NotNull(button);
            Assert.NotNull(palette);
            Point center = button.TranslatePoint(new Point(19, 19), window) ?? throw new InvalidOperationException();
            IReadOnlyList<double> showing = await UiTestSession.ObserveTransitionAsync(palette, Visual.OpacityProperty, 1, () =>
            {
                window.MouseDown(center, MouseButton.Left);
                window.MouseUp(center, MouseButton.Left);
            });
            UiTestSession.SaveFrame(window, "color-popup-v2.png");
            IReadOnlyList<double> hiding = await UiTestSession.ObserveTransitionAsync(palette, Visual.OpacityProperty, 0,
                () => window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null));

            Assert.Contains(showing, value => (value > 0) && (value < 1));
            Assert.Contains(hiding, value => (value > 0) && (value < 1));
            Assert.Equal(DrawingToolId.Arrow, model.Interaction.Tool);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Keyboard_SelectAllWhileEditingText_SelectsTextAndPreservesCaptureSelection()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            PixelRectangle selection = new(300, 200, 600, 350);
            model.Document.SetSelection(selection);
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(150);
            model.Interaction.Tool = DrawingToolId.Text;
            window.MouseDown(new Point(400, 300), MouseButton.Left);
            window.MouseUp(new Point(400, 300), MouseButton.Left);
            TextBox? entry = window.FindControl<TextBox>("TextEntry");
            Assert.NotNull(entry);
            Assert.True(entry.IsVisible);
            entry.Text = "Kadr text";
            entry.CaretIndex = entry.Text.Length;

            window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");

            Assert.Equal(selection, model.Document.Selection);
            Assert.Equal("Kadr text", entry.SelectedText);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Keyboard_SelectAllAndEscape_DeselectsElementAndPreservesToolBeforeClosing()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            SettingsStore settings = await UiTestSession.CreateSettingsAsync(new AppSettings { ExitImmediately = false });
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Interaction.AutoSelectCreatedElements = true;
            model.Document.SetSelection(new PixelRectangle(300, 200, 400, 300));
            EditorWindow window = UiTestSession.CreateEditorWindow(model, settings);
            window.Show();
            await Task.Delay(150);
            int closeRequests = 0;
            model.CloseRequested += (sender, arguments) => closeRequests++;
            model.Interaction.Tool = DrawingToolId.Pencil;
            model.Interaction.Begin(new PixelPoint(400, 300), 7);
            model.Interaction.Complete(new PixelPoint(500, 400), false);
            Assert.NotNull(model.Interaction.SelectedElement);

            window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

            Assert.Equal(model.Document.Bounds, model.Document.Selection);
            Assert.Equal(DrawingToolId.Pencil, model.Interaction.Tool);
            Assert.Null(model.Interaction.SelectedElement);
            Assert.Equal(0, closeRequests);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal(1, closeRequests);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Pointer_OutsideSelection_UsesResizeCursorAndClickDoesNotResize()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Interaction.OutsideSelectionBehavior = OutsideSelectionBehavior.Resize;
            PixelRectangle original = new(400, 300, 200, 100);
            model.Document.SetSelection(original);
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await Task.Delay(150);
            ScreenshotCanvas? surface = window.FindControl<ScreenshotCanvas>("Surface");

            window.MouseMove(new Point(900, 650));
            Assert.Equal("BottomRightCorner", surface?.Cursor?.ToString());
            window.MouseDown(new Point(900, 650), MouseButton.Left);
            window.MouseUp(new Point(900, 650), MouseButton.Left);

            Assert.Equal(original, model.Document.Selection);
            Assert.Equal("BottomRightCorner", surface?.Cursor?.ToString());
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FadeOut_ImmediatelyAfterRequest_DisablesPointerAndKeyboardBeforeAnimationEnds()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await Task.Delay(150);
            int closes = 0;
            model.CloseRequested += (sender, arguments) => closes++;

            Task closing = window.FadeOutAsync();

            Assert.False(window.IsHitTestVisible);
            Assert.False(window.FindControl<Border>("Actions")?.IsHitTestVisible);
            Assert.False(window.FindControl<Border>("ToolsPanel")?.IsHitTestVisible);
            Assert.True(window.IsVisible);
            Assert.False(closing.IsCompleted);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal(0, closes);
            await closing;
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Action_SaveDialogOrOcr_FadesPanelsOutAndBackWithoutDisablingVisualsAbruptly(bool ocr)
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            EditorTestServices services = new()
            {
                SaveCompletion = new TaskCompletionSource<SaveDestination?>(),
                OcrCompletion = new TaskCompletionSource<string>()
            };
            using EditorViewModel model = EditorViewModelTests.Create(services, TestDocuments.Create(1280, 800));
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(180);
            Border? tools = window.FindControl<Border>("ToolsPanel");
            Assert.NotNull(tools);
            await UiTestSession.WaitForAsync(() => tools.Opacity == 1);
            Assert.Equal(1, tools.Opacity);
            Task action = Task.CompletedTask;
            IReadOnlyList<double> fadingOut = await UiTestSession.ObserveTransitionAsync(tools, Visual.OpacityProperty, 0, () =>
            {
                action = ocr ? model.RecognizeCommand.ExecuteAsync(null) : model.SaveCommand.ExecuteAsync(null);
                Assert.True(model.IsLoading);
                Assert.False(tools.IsHitTestVisible);
                Assert.True(tools.IsVisible);
            });
            IReadOnlyList<double> fadingIn = await UiTestSession.ObserveTransitionAsync(tools, Visual.OpacityProperty, 1, () =>
            {
                services.SaveCompletion.SetResult(null);
                services.OcrCompletion.SetResult(string.Empty);
            });
            await action;
            Assert.Contains(fadingOut, value => (value > 0) && (value < 1));
            Assert.Contains(fadingIn, value => (value > 0) && (value < 1));
            Assert.Equal(1, tools.Opacity);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ToolButton_ClickWithoutMovingPointer_ChangesSelectedHoverColorAndUndoRedoOrder()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(new PixelRectangle(300, 200, 600, 350));
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(180);
            Button pencil = window.GetVisualDescendants().OfType<Button>().Single(button =>
                button.CommandParameter is DrawingTool { Id: DrawingToolId.Pencil });
            Point center = pencil.TranslatePoint(new Point(19, 19), window) ?? throw new InvalidOperationException();
            Border hover = pencil.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "HoverLayer");
            Border selected = pencil.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "SelectedLayer");
            IReadOnlyList<double> feedback = await UiTestSession.ObserveTransitionAsync(hover, Visual.OpacityProperty, 0.16,
                () => window.MouseMove(center));
            Assert.Contains(feedback, value => value > 0 && value < 0.16);
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            await UiTestSession.WaitForAsync(() => selected.Opacity == 1);

            Assert.Contains("active", pencil.Classes);
            Assert.Equal(1, selected.Opacity);
            Button undo = window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, model.UndoCommand));
            Button redo = window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, model.RedoCommand));
            Assert.True(redo.TranslatePoint(new Point(), window)?.X > undo.TranslatePoint(new Point(), window)?.X);
            Border? actions = window.FindControl<Border>("Actions");
            Assert.NotNull(actions);
            Assert.Equal(54, actions.Bounds.Width);

            foreach (Button button in actions.GetVisualDescendants().OfType<Button>().Where(button => button.IsVisible))
            {
                Point position = button.TranslatePoint(new Point(button.Bounds.Width / 2, 0), actions) ?? throw new InvalidOperationException();
                Assert.Equal(actions.Bounds.Width / 2, position.X, 1);
            }

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SelectionOutline_ResizingWidthAndHeight_KeepsDashPhaseIndependentOnEveryEdge()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(),
                TestDocuments.Create(1280, 800, new SKColor(40, 60, 90)));
            model.Document.SetSelection(new PixelRectangle(300, 200, 400, 300));
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(160);
            using SKBitmap before = UiTestSession.CapturePixels(window);
            model.Document.SetSelection(new PixelRectangle(300, 200, 450, 340));
            await UiTestSession.PumpFramesAsync(30);

            using SKBitmap after = UiTestSession.CapturePixels(window);

            for (int offset = 8; offset < 180; offset++)
            {
                Assert.Equal(IsWhite(before.GetPixel(300 + offset, 200)), IsWhite(after.GetPixel(300 + offset, 200)));
                Assert.Equal(IsWhite(before.GetPixel(300 + offset, 499)), IsWhite(after.GetPixel(300 + offset, 539)));
                Assert.Equal(IsWhite(before.GetPixel(300, 200 + offset)), IsWhite(after.GetPixel(300, 200 + offset)));
                Assert.Equal(IsWhite(before.GetPixel(699, 200 + offset)), IsWhite(after.GetPixel(749, 200 + offset)));
            }

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Layout_SeveralEdgeSelections_RendersBothPanelsInsideViewport()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(),
                TestDocuments.Create(1280, 800, new SKColor(40, 60, 90)));
            model.Document.SetElements(System.Collections.Immutable.ImmutableArray.Create(
                TestDocuments.Element(DrawingToolId.Arrow, 360, 320, 630, 440),
                TestDocuments.Element(DrawingToolId.Number, 660, 300, 660, 300) with
                {
                    Thickness = 22
                }));
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
            (string Name, PixelRectangle Selection)[] selections = new (string, PixelRectangle)[]
            {
                ("center", new PixelRectangle(300, 200, 600, 350)),
                ("large-bottom-right", new PixelRectangle(300, 200, 980, 600)),
                ("small-bottom-right", new PixelRectangle(1180, 720, 100, 80)),
                ("full", model.Document.Bounds)
            };

            foreach ((string name, PixelRectangle selection) in selections)
            {
                model.Document.SetSelection(selection);
                await UiTestSession.PumpFramesAsync(160);
                Border? actions = window.FindControl<Border>("Actions");
                Border? tools = window.FindControl<Border>("ToolsPanel");
                Assert.NotNull(actions);
                Assert.NotNull(tools);
                Assert.Equal(1, actions.Opacity);
                Assert.Equal(1, tools.Opacity);
                Assert.True(Canvas.GetLeft(actions) >= 0);
                Assert.True(Canvas.GetTop(actions) >= 0);
                Assert.True(Canvas.GetLeft(actions) + actions.Bounds.Width <= 1280);
                Assert.True(Canvas.GetTop(actions) + actions.Bounds.Height <= 800);
                Assert.True(Canvas.GetLeft(tools) >= 0);
                Assert.True(Canvas.GetTop(tools) >= 0);
                Assert.True(Canvas.GetLeft(tools) + tools.Bounds.Width <= 1280);
                Assert.True(Canvas.GetTop(tools) + tools.Bounds.Height <= 800);
                using WriteableBitmap? frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(directory, "refinement-layout-" + name + ".png"), PngBitmapEncoderOptions.Default);
            }

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static bool IsWhite(SKColor color)
    {
        return (color.Red > 200) && (color.Green > 200) && (color.Blue > 200);
    }




}
