using System.Collections.Immutable;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Ocr;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.ViewModels;
using Kadr.Desktop.Views;
using Kadr.Desktop.Views.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using PixelRectangle = Kadr.Desktop.Services.Geometry.PixelRectangle;

namespace Kadr.Desktop.Tests;

[Collection("Avalonia UI")]
public sealed class EditingIterationUiTests
{
    [Fact]
    public async Task Delete_CustomKeyboardAndModifiedMouse_ReplaceDefaultAndSupportUndo()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "Kadr-delete-shortcut-" + Guid.NewGuid().ToString("N"));
            SettingsStore store = new(NullLogger<SettingsStore>.Instance, Path.Combine(directory, "settings.json"));
            try
            {
                await store.SaveAsync(new AppSettings
                {
                    EditorShortcuts = new Dictionary<EditorAction, ShortcutGesture> { [EditorAction.Delete] = new ShortcutGesture(0x72, ShortcutModifiers.None) },
                    AlternativeShortcuts = new Dictionary<EditorAction, ShortcutGesture?> { [EditorAction.Delete] = new ShortcutGesture(4, ShortcutModifiers.Control) }
                }, CancellationToken.None);
                using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800), store);
                model.Document.SetSelection(model.Document.Bounds);
                DrawingElement line = TestDocuments.Element(DrawingToolId.Line, 200, 300, 500, 300);
                model.Document.SetElements(ImmutableArray.Create(line));
                model.Interaction.Begin(new Services.Geometry.PixelPoint(350, 300), 5);
                model.Interaction.Complete(new Services.Geometry.PixelPoint(350, 300), false);
                EditorWindow window = UiTestSession.CreateEditorWindow(model, store);
                window.Show();
                await UiTestSession.PumpFramesAsync(80);

                window.MouseDown(new Point(350, 300), MouseButton.Middle);
                window.MouseUp(new Point(350, 300), MouseButton.Middle);
                Assert.Single(model.Document.Elements);
                Assert.Null(model.Interaction.SelectedId);
                window.MouseDown(new Point(350, 300), MouseButton.Left);
                window.MouseUp(new Point(350, 300), MouseButton.Left);
                Assert.Equal(line.Id, model.Interaction.SelectedId);
                window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
                Assert.Empty(model.Document.Elements);
                model.History.Undo();
                window.MouseDown(new Point(350, 300), MouseButton.Middle, RawInputModifiers.Control);
                window.MouseUp(new Point(350, 300), MouseButton.Middle, RawInputModifiers.Control);
                Assert.Empty(model.Document.Elements);
                model.History.Undo();
                Assert.Equal(line, Assert.Single(model.Document.Elements));
                window.Close();
            }
            finally
            {
                Directory.Delete(directory, true);
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Mouse_DefaultButtons_RightDeletesHoveredObjectAndMiddlePutsAwayTool()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(model.Document.Bounds);
            DrawingElement rectangle = TestDocuments.Element(DrawingToolId.Rectangle, 200, 200, 400, 400);
            DrawingElement line = TestDocuments.Element(DrawingToolId.Line, 600, 250, 800, 250);
            model.Document.SetElements(ImmutableArray.Create(rectangle, line));
            model.Interaction.Tool = DrawingToolId.Arrow;
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(80);

            window.MouseDown(new Point(200, 300), MouseButton.Right);
            window.MouseUp(new Point(200, 300), MouseButton.Right);

            Assert.Equal(line, Assert.Single(model.Document.Elements));
            Assert.Equal(DrawingToolId.Arrow, model.Interaction.Tool);
            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            Assert.Equal(2, model.Document.Elements.Length);
            window.MouseDown(new Point(200, 300), MouseButton.Middle);
            window.MouseUp(new Point(200, 300), MouseButton.Middle);
            Assert.Null(model.Interaction.Tool);
            Assert.Equal(2, model.Document.Elements.Length);
            model.Interaction.Tool = DrawingToolId.Arrow;
            window.MouseDown(new Point(500, 600), MouseButton.Right);
            window.MouseDown(new Point(500, 600), MouseButton.Middle);
            Assert.Null(model.Interaction.Tool);
            window.MouseUp(new Point(500, 600), MouseButton.Middle);
            window.MouseUp(new Point(500, 600), MouseButton.Right);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Preview_AltWithActiveTool_DimsAndRestoresWhileShiftLeavesEditorVisible()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            bool alt = false;
            SettingsStore store = UiTestSession.CreateSettingsStore();
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(new PixelRectangle(200, 200, 700, 350));
            model.Interaction.Tool = DrawingToolId.Arrow;
            EditorWindow window = new(model, model.Document.Desktop.Monitors[0], store, key => key == 0x12 && alt);
            window.Show();
            window.Activate();
            await UiTestSession.PumpFramesAsync(80);

            alt = true;
            await UiTestSession.WaitForAsync(() => model.IsPreview);
            await UiTestSession.PumpFramesAsync(120);
            Assert.Equal(0, window.FindControl<Border>("Actions")?.Opacity);
            UiTestSession.SaveFrame(window, "alt-preview-1.7.png");
            alt = false;
            await UiTestSession.WaitForAsync(() => !model.IsPreview);
            window.KeyPress(Key.LeftShift, RawInputModifiers.Shift, PhysicalKey.ShiftLeft, null);
            await UiTestSession.PumpFramesAsync(80);
            Assert.False(model.IsPreview);
            Assert.Equal(DrawingToolId.Arrow, model.Interaction.Tool);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Preview_CustomBindingAppliedToOpenEditor_UsesHeldKeyAndIgnoresTextInput()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "Kadr-preview-" + Guid.NewGuid().ToString("N"));
            SettingsStore store = new(NullLogger<SettingsStore>.Instance, Path.Combine(directory, "settings.json"));
            HashSet<int> pressed = [];
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800), store);
            model.Document.SetSelection(new PixelRectangle(200, 200, 700, 350));
            model.Interaction.Tool = DrawingToolId.Arrow;
            EditorWindow window = new(model, model.Document.Desktop.Monitors[0], store, pressed.Contains);
            try
            {
                window.Show();
                window.Activate();
                await UiTestSession.PumpFramesAsync(80);
                await store.UpdateAsync(current => current with
                {
                    EditorShortcuts = new Dictionary<EditorAction, ShortcutGesture>
                    {
                        [EditorAction.Preview] = new ShortcutGesture(0x77, ShortcutModifiers.Control)
                    },
                    AlternativeShortcuts = new Dictionary<EditorAction, ShortcutGesture?>
                    {
                        [EditorAction.Preview] = new ShortcutGesture(0x10, ShortcutModifiers.None)
                    }
                }, CancellationToken.None);

                pressed.Add(0x12);
                await UiTestSession.PumpFramesAsync(80);
                Assert.False(model.IsPreview);
                pressed.Clear();
                pressed.Add(0x77);
                pressed.Add(0x11);
                await UiTestSession.WaitForAsync(() => model.IsPreview);
                Assert.Equal(DrawingToolId.Arrow, model.Interaction.Tool);
                pressed.Remove(0x11);
                await UiTestSession.WaitForAsync(() => !model.IsPreview);
                pressed.Clear();
                pressed.Add(0x10);
                await UiTestSession.WaitForAsync(() => model.IsPreview);
                pressed.Clear();
                await UiTestSession.WaitForAsync(() => !model.IsPreview);

                window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
                window.MouseDown(new Point(300, 300), MouseButton.Left);
                window.MouseUp(new Point(300, 300), MouseButton.Left);
                TextBox text = window.FindControl<TextBox>("TextEntry") ?? throw new InvalidOperationException();
                Assert.True(text.IsVisible);
                pressed.Add(0x10);
                await UiTestSession.PumpFramesAsync(80);
                Assert.False(model.IsPreview);
            }
            finally
            {
                window.Close();
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(RawInputModifiers.Control)]
    [InlineData(RawInputModifiers.Shift)]
    public async Task Resize_ExistingArrowWithModifier_ConstrainsEndpointThroughRoutedInput(RawInputModifiers modifiers)
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(model.Document.Bounds);
            model.Interaction.AutoSelectCreatedElements = true;
            model.Interaction.Tool = DrawingToolId.Arrow;
            model.Interaction.Begin(new Services.Geometry.PixelPoint(200, 300), 5);
            model.Interaction.Complete(new Services.Geometry.PixelPoint(500, 300), false);
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(80);

            window.MouseDown(new Point(500, 300), MouseButton.Left, modifiers);
            window.MouseMove(new Point(750, 330), modifiers);
            window.MouseUp(new Point(750, 330), MouseButton.Left, modifiers);

            Assert.Equal(300, Assert.Single(model.Document.Elements).Points[^1].Y, 6);
            Assert.Equal(750, Assert.Single(model.Document.Elements).Points[^1].X, 6);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Settings_RowClickThemeAndFade_UseWholeRowAndConsistentInputStates()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using HttpClient http = new();
            SettingsStore store = UiTestSession.CreateSettingsStore();
            SettingsViewModel model = new(store, new TestSettingsApplication(), NullLogger<SettingsViewModel>.Instance, true);
            SettingsWindow window = new() { DataContext = model };
            window.Show();
            await UiTestSession.PumpFramesAsync(80);
            Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
            Assert.True(window.ExtendClientAreaToDecorationsHint);
            Assert.True(window.CanResize);
            Assert.Equal("Kadr", window.Title);
            GeneralSettingsView general = window.GetVisualDescendants().OfType<GeneralSettingsView>().Single();
            ComboBox combo = general.FindControl<ComboBox>("CaptureModeBox") ?? throw new InvalidOperationException();
            window.MouseMove(combo.TranslatePoint(new Point(30, 18), window) ?? throw new InvalidOperationException());
            await UiTestSession.PumpFramesAsync(150);
            Border background = combo.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "Background");
            Assert.Equal(Color.Parse("#24354D"), Assert.IsAssignableFrom<ISolidColorBrush>(background.Background).Color);
            Assert.Equal("Hand", combo.Cursor?.ToString());
            combo.IsDropDownOpen = true;
            await UiTestSession.PumpFramesAsync(60);
            ComboBoxItem item = Assert.IsType<ComboBoxItem>(combo.ContainerFromIndex(combo.SelectedIndex));
            ContentPresenter presenter = item.GetVisualDescendants().OfType<ContentPresenter>().First(control => control.Name == "PART_ContentPresenter");
            Assert.Equal(Color.Parse("#40365C"), Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
            Assert.Equal("Hand", item.Cursor?.ToString());
            TopLevel popup = TopLevel.GetTopLevel(item) ?? throw new InvalidOperationException();
            popup.MouseMove(item.TranslatePoint(new Point(20, item.Bounds.Height / 2), popup) ?? throw new InvalidOperationException());
            await UiTestSession.PumpFramesAsync(100);
            Assert.Equal(Color.Parse("#605184"), Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
            UiTestSession.SaveFrame(popup, "settings-dropdown-1.7.png");
            combo.IsDropDownOpen = false;
            model.Section = SettingsSection.Toolbar;
            await UiTestSession.PumpFramesAsync(80);
            ToolbarSettingsView toolbar = window.GetVisualDescendants().OfType<ToolbarSettingsView>().Single();
            ToggleSwitch row = toolbar.GetVisualDescendants().OfType<ToggleSwitch>().First();
            ToolbarItemViewModel setting = Assert.IsType<ToolbarItemViewModel>(row.DataContext);
            Point farEdge = row.TranslatePoint(new Point(row.Bounds.Width - 6, row.Bounds.Height / 2), window) ?? throw new InvalidOperationException();
            window.MouseDown(farEdge, MouseButton.Left);
            window.MouseUp(farEdge, MouseButton.Left);
            Assert.False(setting.IsVisible);
            Point text = row.TranslatePoint(new Point(100, row.Bounds.Height / 2), window) ?? throw new InvalidOperationException();
            window.MouseDown(text, MouseButton.Left);
            window.MouseUp(text, MouseButton.Left);
            Assert.True(setting.IsVisible);
            Point knob = row.TranslatePoint(new Point(20, row.Bounds.Height / 2), window) ?? throw new InvalidOperationException();
            window.MouseDown(knob, MouseButton.Left);
            window.MouseUp(knob, MouseButton.Left);
            Assert.False(setting.IsVisible);
            ScrollViewer scroll = window.FindControl<ScrollViewer>("SectionScroll") ?? throw new InvalidOperationException();
            ScrollContentPresenter content = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
            LinearGradientBrush mask = Assert.IsType<LinearGradientBrush>(content.OpacityMask);
            Assert.Equal(0, mask.GradientStops[0].Color.A);
            Assert.Equal(0, mask.GradientStops[^1].Color.A);
            scroll.Offset = new Vector(0, 80);
            await UiTestSession.PumpFramesAsync(150);
            Assert.Equal(0, mask.GradientStops[0].Color.A);
            UiTestSession.SaveFrame(window, "settings-toolbar-1.7.png");
            ClickSection(window, SettingsSection.Shortcuts);
            await UiTestSession.PumpFramesAsync(80);
            ShortcutSettingsView shortcuts = window.GetVisualDescendants().OfType<ShortcutSettingsView>().Single();
            TextBox query = shortcuts.GetVisualDescendants().OfType<TextBox>().First();
            window.MouseMove(query.TranslatePoint(new Point(50, 18), window) ?? throw new InvalidOperationException());
            await UiTestSession.PumpFramesAsync(150);
            Border queryBackground = query.GetVisualDescendants().OfType<Border>().First(border => border.Name == "PART_BorderElement");
            Assert.Equal(Color.Parse("#24354D"), Assert.IsAssignableFrom<ISolidColorBrush>(queryBackground.Background).Color);
            UiTestSession.SaveFrame(window, "settings-shortcuts-1.7.png");
            ClickSection(window, SettingsSection.Recognition);
            await UiTestSession.PumpFramesAsync(80);
            RecognitionSettingsView recognition = window.GetVisualDescendants().OfType<RecognitionSettingsView>().Single();
            ToggleSwitch language = recognition.GetVisualDescendants().OfType<ToggleSwitch>().First();
            OcrLanguageViewModel languageModel = Assert.IsType<OcrLanguageViewModel>(language.DataContext);
            bool before = languageModel.IsSelected;
            Point edge = language.TranslatePoint(new Point(language.Bounds.Width - 3, language.Bounds.Height / 2), window) ?? throw new InvalidOperationException();
            window.MouseDown(edge, MouseButton.Left);
            window.MouseUp(edge, MouseButton.Left);
            Assert.Equal(!before, languageModel.IsSelected);
            UiTestSession.SaveFrame(window, "settings-recognition-1.7.png");
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static void ClickSection(Window window, SettingsSection section)
    {
        Button button = window.GetVisualDescendants().OfType<Button>().Single(control => control.CommandParameter is SettingsSection value && value == section);
        Point center = button.TranslatePoint(new Point(60, 20), window) ?? throw new InvalidOperationException();
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
    }
}
