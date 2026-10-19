using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

using Kadr.Desktop.Controls;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Ocr;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.ViewModels;
using Kadr.Desktop.Views;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Kadr.Desktop.Tests;

[Collection("Avalonia UI")]
public sealed class UiStabilityTests
{
    [Fact]
    public async Task Dropdown_SelectedItemBringIntoView_ScrollsOnlyPopupAndKeepsSettingsOffset()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            StableComboBox combo = new() { ItemsSource = Enumerable.Range(0, 80).ToArray(), SelectedIndex = 60 };
            StackPanel content = new() { Children = { new Border { Height = 600 }, combo, new Border { Height = 900 } } };
            ScrollViewer scroll = new() { Content = content };
            Window window = new() { Width = 600, Height = 500, Content = scroll };
            try
            {
                window.Show();
                await UiTestSession.PumpFramesAsync(80);
                scroll.Offset = new Vector(0, 500);
                await UiTestSession.PumpFramesAsync(30);
                combo.IsDropDownOpen = true;
                await UiTestSession.PumpFramesAsync(100);
                Assert.Equal(500, scroll.Offset.Y);
                ComboBoxItem selected = Assert.IsType<ComboBoxItem>(combo.ContainerFromIndex(60));
                selected.BringIntoView();
                await UiTestSession.PumpFramesAsync(50);
                Assert.Equal(500, scroll.Offset.Y);
                combo.IsDropDownOpen = false;
                combo.BringIntoView();
                await UiTestSession.PumpFramesAsync(30);
                Assert.Equal(500, scroll.Offset.Y);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Palette_RevealAndReverse_AnimateEachRowAsOneGroupInOrder()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(() =>
        {
            Control[] items = Enumerable.Range(0, 10).Select(index => (Control)new Border()).ToArray();
            PalettePanel palette = new() { Transitions = null };
            palette.InitializeItems(items);
            foreach (double reveal in new double[] { 0.2, 0.45, 0.7, 1, 0.7, 0.45, 0.2, 0 })
            {
                palette.Reveal = reveal;
                for (int row = 0; row < 3; row++)
                {
                    Assert.Equal(items[row * 3].Opacity, items[row * 3 + 1].Opacity);
                    Assert.Equal(items[row * 3].Opacity, items[row * 3 + 2].Opacity);
                }
                Assert.True(items[0].Opacity >= items[3].Opacity);
                Assert.True(items[3].Opacity >= items[6].Opacity);
                Assert.True(items[6].Opacity >= items[9].Opacity);
            }
            Assert.All(items, item => Assert.Equal(0, item.Opacity));
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ShortcutHint_OpeningAndCancellation_AnimateBeforePopupIsRemoved()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            SettingsViewModel model = new(UiTestSession.CreateSettingsStore(), new TestSettingsApplication(),
                 NullLogger<SettingsViewModel>.Instance, true);
            model.Section = SettingsSection.Shortcuts;
            model.ShortcutQuery = model.Shortcuts[1].Name;
            SettingsWindow window = new() { DataContext = model };
            try
            {
                window.Show();
                await UiTestSession.PumpFramesAsync(80);
                Button field = window.GetVisualDescendants().OfType<Button>().First(button => button.DataContext is ShortcutBindingViewModel && button.Command is null);
                AnimatedPopupPanel panel = window.FindControl<AnimatedPopupPanel>("RecordingHintPanel") ?? throw new InvalidOperationException();
                Popup popup = window.FindControl<Popup>("ShortcutRecordingHint") ?? throw new InvalidOperationException();
                IReadOnlyList<double> opening = await UiTestSession.ObserveTransitionAsync(panel, AnimatedPopupPanel.RevealProperty, 1,
                    () => field.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                Assert.Contains(opening, value => value > 0 && value < 1);
                ProgressBar progress = window.FindControl<ProgressBar>("ClearShortcutProgress") ?? throw new InvalidOperationException();
                Assert.Equal(Color.Parse("#8A75F5"), Assert.IsAssignableFrom<ISolidColorBrush>(progress.Foreground).Color);
                field.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(popup.IsOpen);
                Assert.False(model.IsRecordingShortcut);
                await UiTestSession.WaitForAsync(() => !popup.IsOpen);
                Assert.Equal(0, panel.Opacity);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(ColorPickerTab.Rgb)]
    [InlineData(ColorPickerTab.Hsl)]
    [InlineData(ColorPickerTab.Hex)]
    public async Task ColorPicker_LayoutsAndAllTabs_KeepRgbaAndHexInSync(ColorPickerTab tab)
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            ColorPickerWindow window = new(0x803b82f6, tab);
            try
            {
                window.Show();
                await UiTestSession.PumpFramesAsync(100);
                ColorPickerViewModel model = Assert.IsType<ColorPickerViewModel>(window.DataContext);
                Assert.Equal((int)tab, model.SelectedTab);
                Button[] tabs = (window.FindControl<Grid>("ColorTabs") ?? throw new InvalidOperationException()).Children.OfType<Button>().ToArray();
                Assert.True(tabs[1].Bounds.X - tabs[0].Bounds.Right >= 6);
                Assert.True(tabs[2].Bounds.X - tabs[1].Bounds.Right >= 6);
                foreach (int index in new int[] { 0, 1 })
                {
                    model.SelectedTab = index;
                    await UiTestSession.PumpFramesAsync(25);
                    foreach (NumericUpDown input in window.GetVisualDescendants().OfType<NumericUpDown>().Where(control => control.IsEffectivelyVisible))
                    {
                        Assert.Equal(36, input.Bounds.Height);
                        Assert.Equal(VerticalAlignment.Center, input.VerticalContentAlignment);
                    }
                }
                Assert.Equal(128, window.FindControl<NumericUpDown>("AlphaInput")?.Value);
                Assert.Equal(59, window.FindControl<NumericUpDown>("RedInput")?.Value);
                SaturationValueBox spectrum = window.FindControl<SaturationValueBox>("Spectrum") ?? throw new InvalidOperationException();
                Point huePoint = spectrum.TranslatePoint(new Point(160, 18), window) ?? throw new InvalidOperationException();
                window.MouseDown(huePoint, MouseButton.Left);
                window.MouseUp(huePoint, MouseButton.Left);
                Assert.Equal(270, model.Hue, 4);
                model.SelectedTab = 1;
                await UiTestSession.PumpFramesAsync(30);
                Assert.True(model.IsHslMode);
                model.Lightness = 50;
                model.SelectedTab = 2;
                await UiTestSession.PumpFramesAsync(30);
                TextBox hex = window.FindControl<TextBox>("HexInput") ?? throw new InvalidOperationException();
                Assert.True(hex.IsEffectivelyVisible);
                hex.Text = "#00FF0040";
                await UiTestSession.PumpFramesAsync(30);
                Assert.Equal(0x4000ff00u, model.ColorValue);
                Assert.Equal(255, window.FindControl<NumericUpDown>("GreenInput")?.Value);
                Assert.Equal(64, window.FindControl<NumericUpDown>("AlphaInput")?.Value);
                model.SelectedTab = 0;
                await UiTestSession.PumpFramesAsync(30);
                UiTestSession.SaveFrame(window, $"color-{tab}-1.10.png");
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(ColorPickerTab.Rgb)]
    [InlineData(ColorPickerTab.Hsl)]
    [InlineData(ColorPickerTab.Hex)]
    public async Task ColorPicker_TallSliderEdgesAndFieldLayout_StayInteractiveWithoutOverlap(ColorPickerTab tab)
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            ColorPickerWindow window = new(0xff3b82f6, tab);
            try
            {
                window.Show();
                await UiTestSession.PumpFramesAsync(60);
                ColorPickerViewModel pickerModel = Assert.IsType<ColorPickerViewModel>(window.DataContext);
                pickerModel.SelectedTab = 0;
                await UiTestSession.PumpFramesAsync(30);
                ColorSlider slider = window.FindControl<ColorSlider>("RedSlider") ?? throw new InvalidOperationException();
                Assert.True(slider.Bounds.Height >= 36);
                foreach (double y in new double[] { 1, slider.Bounds.Height - 1 })
                {
                    Point point = slider.TranslatePoint(new Point(14 + (slider.Bounds.Width - 28) * 0.75, y), window) ?? throw new InvalidOperationException();
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                    Assert.InRange(slider.Value / slider.Maximum, 0.74, 0.76);
                    slider.Value = 0;
                }
                ColorPickerViewModel model = Assert.IsType<ColorPickerViewModel>(window.DataContext);
                model.SetColor(0xff3b82f6);
                await UiTestSession.PumpFramesAsync(40);
                UiTestSession.SaveFrame(window, "color-reference-1.10.png");
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ColorPicker_InnerFieldCorner_UsesUnrotatedSquareCoordinates()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            ColorPickerWindow window = new(0xff3b82f6);
            try
            {
                window.Show();
                await UiTestSession.PumpFramesAsync(60);
                SaturationValueBox spectrum = window.FindControl<SaturationValueBox>("Spectrum") ?? throw new InvalidOperationException();
                Point point = spectrum.TranslatePoint(new Point(84, 84), window) ?? throw new InvalidOperationException();

                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);

                Assert.InRange(spectrum.Saturation, 0.03, 0.06);
                Assert.InRange(spectrum.Value, 0.94, 0.97);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ColorPicker_CancelAndReopen_RemembersTabAcrossEditorAndStoreReload()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "Kadr-color-tab-" + Guid.NewGuid().ToString("N"));
            SettingsStore store = new(NullLogger<SettingsStore>.Instance, Path.Combine(directory, "settings.json"));
            using EditorViewModel editor = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1000, 700), store);
            EditorWindow window = UiTestSession.CreateEditorWindow(editor, store);
            try
            {
                window.Show();
                await UiTestSession.PumpFramesAsync(60);
                Button open = window.FindControl<Button>("CustomColorButton") ?? throw new InvalidOperationException();
                open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await UiTestSession.PumpFramesAsync(40);
                ColorPickerWindow picker = window.OwnedWindows.OfType<ColorPickerWindow>().Single();
                ColorPickerViewModel model = Assert.IsType<ColorPickerViewModel>(picker.DataContext);
                model.SelectedTab = 2;
                model.CancelCommand.Execute(null);
                await UiTestSession.WaitForAsync(() => store.Current.ColorPickerTab == ColorPickerTab.Hex);

                open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await UiTestSession.PumpFramesAsync(40);
                ColorPickerWindow reopened = window.OwnedWindows.OfType<ColorPickerWindow>().Single();
                Assert.Equal(ColorPickerTab.Hex, reopened.SelectedTab);
                Assert.True(reopened.FindControl<TextBox>("HexInput")?.IsEffectivelyVisible);
                reopened.Close();
                SettingsStore reloaded = new(NullLogger<SettingsStore>.Instance, Path.Combine(directory, "settings.json"));
                await reloaded.LoadAsync(CancellationToken.None);
                Assert.Equal(ColorPickerTab.Hex, reloaded.Current.ColorPickerTab);
            }
            finally
            {
                window.Close();
                Directory.Delete(directory, true);
            }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(DrawingToolId.Pencil, true)]
    [InlineData(DrawingToolId.Arrow, true)]
    [InlineData(DrawingToolId.Line, true)]
    [InlineData(DrawingToolId.Rectangle, true)]
    [InlineData(DrawingToolId.Ellipse, true)]
    [InlineData(DrawingToolId.Number, true)]
    [InlineData(DrawingToolId.Text, true)]
    [InlineData(DrawingToolId.Marker, false)]
    public async Task ToolPreview_Shadow_MatchesToolStyle(DrawingToolId tool, bool expectedShadow)
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1000, 700));
            model.Interaction.Tool = tool;
            model.Interaction.Thickness = 12;
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            try
            {
                window.Show();
                await UiTestSession.PumpFramesAsync(80);
                window.MouseMove(new Point(400, 300));
                await UiTestSession.PumpFramesAsync(30);
                using SKBitmap image = UiTestSession.CapturePixels(window);
                int grayPixels = Enumerable.Range(260, 80).Sum(y => Enumerable.Range(360, 80).Count(x =>
                {
                    SKColor color = image.GetPixel(x, y);
                    return color.Red < 250 && color.Red == color.Green && color.Green == color.Blue;
                }));
                Assert.Equal(expectedShadow, grayPixels > 0);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }
}
