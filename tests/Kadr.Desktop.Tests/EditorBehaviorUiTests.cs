using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

using Kadr.Desktop.Controls;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Ocr;
using Kadr.Desktop.Services.Rendering;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.ViewModels;
using Kadr.Desktop.Views;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Kadr.Desktop.Tests;

[Collection("Avalonia UI")]
public sealed class EditorBehaviorUiTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Settings_EditorFlags_ControlExitAndNativePointer(bool immediateExit, bool hidePointer)
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "Kadr-editor-flags-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "settings.json");
            SettingsStore settings = new(NullLogger<SettingsStore>.Instance, path);
            try
            {
                await settings.SaveAsync(new AppSettings { ExitImmediately = immediateExit, HideCursorWhileDrawing = hidePointer, AutoSelectCreatedElements = true }, CancellationToken.None);
                using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800), settings);
                model.Document.SetSelection(new PixelRectangle(300, 200, 600, 350));
                int closeRequests = 0;
                model.CloseRequested += (_, _) => closeRequests++;
                EditorWindow window = UiTestSession.CreateEditorWindow(model, settings);
                window.Show();
                await UiTestSession.PumpFramesAsync(120);
                window.KeyPress(Key.D1, RawInputModifiers.None, PhysicalKey.Digit1, "1");
                window.MouseMove(new Point(400, 300));
                ScreenshotCanvas surface = window.FindControl<ScreenshotCanvas>("Surface") ?? throw new InvalidOperationException();
                Assert.True(surface.ShowDrawingPointer);
                window.MouseDown(new Point(400, 300), MouseButton.Left);
                Assert.Equal(hidePointer ? "None" : "Cross", surface.Cursor?.ToString());
                window.MouseUp(new Point(600, 350), MouseButton.Left);
                Assert.NotNull(model.Interaction.SelectedElement);

                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

                Assert.Equal(immediateExit ? 1 : 0, closeRequests);
                if (!immediateExit)
                {
                    Assert.Null(model.Interaction.SelectedElement);
                }
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
    public async Task Wheel_OldSelectedElementAndInlineText_UpdatesSizeThroughRoutedInput()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(new PixelRectangle(300, 200, 600, 350));
            model.Document.SetElements(System.Collections.Immutable.ImmutableArray.Create(TestDocuments.Element(DrawingToolId.Blur, 400, 300, 500, 400)));
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(120);
            window.MouseDown(new Point(450, 350), MouseButton.Left);
            window.MouseUp(new Point(450, 350), MouseButton.Left);
            DrawingElement original = Assert.Single(model.Document.Elements);

            window.MouseWheel(new Point(450, 350), new Vector(0, 1));
            await UiTestSession.WaitForAsync(() => model.Document.Elements[0].BlurStrength == WheelSizeScale.GetTarget(original.BlurStrength, 1, 1, 128));

            Assert.Equal(WheelSizeScale.GetTarget(original.BlurStrength, 1, 1, 128), Assert.Single(model.Document.Elements).BlurStrength);
            Assert.Equal(DrawingDefaults.Thickness, model.Interaction.Thickness);
            window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
            window.MouseDown(new Point(600, 350), MouseButton.Left);
            window.MouseUp(new Point(600, 350), MouseButton.Left);
            TextBox entry = window.FindControl<TextBox>("TextEntry") ?? throw new InvalidOperationException();
            entry.Text = "Wheel text";
            await UiTestSession.PumpFramesAsync(30);
            double originalFont = entry.FontSize;
            window.MouseWheel(new Point(620, 350), new Vector(0, 1));
            await UiTestSession.PumpFramesAsync(30);
            Assert.True(entry.FontSize > originalFont);
            bool? horizontalHandled = null;
            entry.AddHandler(InputElement.PointerWheelChangedEvent, (_, arguments) => horizontalHandled = arguments.Handled, RoutingStrategies.Tunnel, true);
            window.MouseWheel(new Point(620, 350), new Vector(1, 0));
            Assert.False(horizontalHandled);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            Assert.Single(model.Document.Elements);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Element_HoverBodyAndFullScreenSelection_UsesMoveCursorAndRightAlignedPanels()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(new PixelRectangle(300, 200, 600, 350));
            model.Document.SetElements(System.Collections.Immutable.ImmutableArray.Create(TestDocuments.Element(DrawingToolId.Blur, 400, 300, 500, 400)));
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(120);

            window.MouseMove(new Point(450, 350));

            ScreenshotCanvas surface = window.FindControl<ScreenshotCanvas>("Surface") ?? throw new InvalidOperationException();
            Assert.Equal("SizeAll", surface.Cursor?.ToString());
            window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
            await UiTestSession.PumpFramesAsync(120);
            Border actions = window.FindControl<Border>("Actions") ?? throw new InvalidOperationException();
            Border tools = window.FindControl<Border>("ToolsPanel") ?? throw new InvalidOperationException();
            Assert.True(Canvas.GetLeft(tools) > 500);
            Assert.True(Canvas.GetLeft(tools) + tools.Bounds.Width < Canvas.GetLeft(actions));
            UiTestSession.SaveFrame(window, "full-screen-tools-1.3.png");
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ColorPicker_OriginalAndAdvancedValues_RemainVisibleAndRejectInvalidHex()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            ColorPickerWindow picker = new(0xffec3e54);
            picker.Show();
            await UiTestSession.PumpFramesAsync(100);
            ColorChip original = picker.FindControl<ColorChip>("OldColor") ?? throw new InvalidOperationException();
            Assert.True(original.Bounds.Width > 100 && original.Bounds.Height > 30);
            UiTestSession.SaveFrame(picker, "color-picker-1.3.png");
            Grid advanced = picker.FindControl<Grid>("RgbFields") ?? throw new InvalidOperationException();
            Assert.True(advanced.IsVisible);
            ((ColorPickerViewModel)(picker.DataContext ?? throw new InvalidOperationException())).SelectedTab = 2;
            await UiTestSession.PumpFramesAsync(30);
            TextBox hex = picker.FindControl<TextBox>("HexInput") ?? throw new InvalidOperationException();
            hex.Text = "invalid";
            Button accept = picker.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("primary"));
            Point location = accept.TranslatePoint(new Point(50, 15), picker) ?? throw new InvalidOperationException();
            picker.MouseDown(location, MouseButton.Left);
            picker.MouseUp(location, MouseButton.Left);
            Assert.True(picker.IsVisible);
            Assert.True(picker.FindControl<TextBlock>("HexError")?.IsVisible);
            picker.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Button_HoverPressReleaseLeave_IntermediateRenderedPixelsNeverFlashWhite()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(new PixelRectangle(300, 200, 600, 350));
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(160);
            Button button = window.GetVisualDescendants().OfType<Button>().Single(item => item.CommandParameter is PencilTool);
            Point center = button.TranslatePoint(new Point(19, 19), window) ?? throw new InvalidOperationException();
            Point sample = button.TranslatePoint(new Point(6, 6), window) ?? throw new InvalidOperationException();
            List<SKColor> samples = [];
            Action[] transitions =
            [
                () => window.MouseMove(center),
                () => window.MouseDown(center, MouseButton.Left),
                () => window.MouseUp(center, MouseButton.Left),
                () => window.MouseMove(new Point(1000, 100))
            ];

            foreach (Action change in transitions)
            {
                change();
                for (int index = 0; index < 12; index++)
                {
                    await UiTestSession.PumpFramesAsync(8);
                    using SKBitmap image = UiTestSession.CapturePixels(window);
                    samples.Add(image.GetPixel((int)sample.X, (int)sample.Y));
                }
            }

            Assert.True(samples.Distinct().Count() >= 4);
            Assert.All(samples, color => Assert.True(color.Red < 180 && color.Green < 180 && color.Blue < 180, color.ToString()));
            UiTestSession.SaveFrame(window, "button-feedback-1.3.png");
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Drawing_PressAndRelease_HidesCustomPointerAndBlurCrosshairOnlyDuringCreation()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            SettingsStore settings = await UiTestSession.CreateSettingsAsync(new AppSettings { HideCursorWhileDrawing = true, AutoSelectCreatedElements = false });
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800), settings);
            model.Document.SetSelection(new PixelRectangle(300, 200, 600, 350));
            EditorWindow window = UiTestSession.CreateEditorWindow(model, settings);
            window.Show();
            await UiTestSession.PumpFramesAsync(140);
            ScreenshotCanvas surface = window.FindControl<ScreenshotCanvas>("Surface") ?? throw new InvalidOperationException();
            window.KeyPress(Key.D1, RawInputModifiers.None, PhysicalKey.Digit1, "1");
            window.MouseMove(new Point(400, 300));
            Assert.True(surface.ShowDrawingPointer);

            window.MouseDown(new Point(400, 300), MouseButton.Left);

            Assert.False(surface.ShowDrawingPointer);
            Assert.Equal("None", surface.Cursor?.ToString());
            window.MouseUp(new Point(600, 350), MouseButton.Left);
            Assert.True(surface.ShowDrawingPointer);
            window.KeyPress(Key.B, RawInputModifiers.None, PhysicalKey.B, "b");
            window.MouseMove(new Point(400, 300));
            Assert.Equal("Cross", surface.Cursor?.ToString());
            window.MouseDown(new Point(400, 300), MouseButton.Left);
            Assert.Equal("None", surface.Cursor?.ToString());
            window.MouseUp(new Point(600, 350), MouseButton.Left);
            Assert.Equal("Cross", surface.Cursor?.ToString());
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Text_SelectionHighlight_IsTranslucentAndDoesNotReplaceRenderedGlyphColor()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(new PixelRectangle(300, 200, 600, 350));
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(120);
            window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
            window.MouseDown(new Point(400, 300), MouseButton.Left);
            window.MouseUp(new Point(400, 300), MouseButton.Left);
            TextBox entry = window.FindControl<TextBox>("TextEntry") ?? throw new InvalidOperationException();
            entry.Text = "Selected text";
            await UiTestSession.PumpFramesAsync(40);
            entry.SelectAll();

            await UiTestSession.PumpFramesAsync(60);

            Color highlight = Assert.IsAssignableFrom<ISolidColorBrush>(entry.SelectionBrush).Color;
            Assert.InRange(highlight.A, (byte)1, (byte)120);
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(entry.SelectionForegroundBrush).Color.A);
            UiTestSession.SaveFrame(window, "selected-text-1.3.png");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.MouseMove(new Point(404, 300));
            ScreenshotCanvas surface = window.FindControl<ScreenshotCanvas>("Surface") ?? throw new InvalidOperationException();
            Assert.Equal("SizeAll", surface.Cursor?.ToString());
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Settings_NavigationAndColor_RendersEachSectionWithFixedFooter()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using HttpClient http = new();
            SettingsStore settings = UiTestSession.CreateSettingsStore();
            SettingsViewModel model = new(settings, new TestSettingsApplication(), NullLogger<SettingsViewModel>.Instance, true);
            SettingsWindow window = new() { DataContext = model };
            window.Show();
            await UiTestSession.PumpFramesAsync(160);

            foreach (SettingsSection section in Enum.GetValues<SettingsSection>())
            {
                Button navigation = window.GetVisualDescendants().OfType<Button>().Single(button => button.CommandParameter is SettingsSection value && value == section);
                Point center = navigation.TranslatePoint(new Point(60, 20), window) ?? throw new InvalidOperationException();
                window.MouseDown(center, MouseButton.Left);
                window.MouseUp(center, MouseButton.Left);
                await UiTestSession.PumpFramesAsync(100);

                Assert.Equal(section, model.Section);
                UiTestSession.SaveFrame(window, $"settings-{section.ToString().ToLowerInvariant()}-1.3.png");
            }

            Assert.True(model.ShowCursorWhileDrawing);
            Assert.True(model.AutoSelectCreatedElements);
            Assert.True(model.ExitImmediately);
            model.Section = SettingsSection.Editor;
            await UiTestSession.PumpFramesAsync(40);
            ToggleSwitch hideCursor = window.GetVisualDescendants().OfType<ToggleSwitch>().Single(control => control.Name == "ShowDrawingCursorSwitch");
            Point switchCenter = hideCursor.TranslatePoint(new Point(12, 12), window) ?? throw new InvalidOperationException();
            window.MouseDown(switchCenter, MouseButton.Left);
            window.MouseUp(switchCenter, MouseButton.Left);
            await UiTestSession.PumpFramesAsync(40);
            Assert.False(model.ShowCursorWhileDrawing);
            model.ExitShortcut.Gesture = new ShortcutGesture(0x7b, ShortcutModifiers.None);
            await UiTestSession.PumpFramesAsync(40);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text is string content && content.Contains("F12", StringComparison.Ordinal));
            Button save = window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, model.SaveCommand));
            Point location = save.TranslatePoint(new Point(), window) ?? throw new InvalidOperationException();
            Assert.True(location.Y + save.Bounds.Height <= window.ClientSize.Height);
            model.LanguageQuery = "rus";
            Assert.Equal("rus", Assert.Single(model.VisibleLanguages).Code);
            window.Close();
            return true;
        }, CancellationToken.None);
    }
}
