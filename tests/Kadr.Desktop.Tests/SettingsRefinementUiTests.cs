using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

using Kadr.Desktop.Controls;
using Kadr.Desktop.Controls.Scrolling;
using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Ocr;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.ViewModels;
using Kadr.Desktop.Views;
using Kadr.Desktop.Views.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Kadr.Desktop.Tests;

[Collection("Avalonia UI")]
public sealed class SettingsRefinementUiTests
{
    [Fact]
    public async Task Settings_GeneralAndToolbar_KeepLastAutostartBoldHeadingAndSeparateGroups()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using HttpClient http = new();
            SettingsViewModel model = new(UiTestSession.CreateSettingsStore(), new TestSettingsApplication(),
                new OcrModelStore(http), NullLogger<SettingsViewModel>.Instance, true);
            SettingsWindow window = new() { DataContext = model };
            try
            {
                window.Show();
                await UiTestSession.PumpFramesAsync(100);
                GeneralSettingsView general = window.GetVisualDescendants().OfType<GeneralSettingsView>().Single();
                StackPanel panel = Assert.IsType<StackPanel>(general.Content);
                Assert.IsType<ToggleSwitch>(panel.Children[^1]);
                TextBlock outside = general.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == UiText.Get("Kadr.OutsideSelection"));
                Assert.Equal(FontWeight.SemiBold, outside.FontWeight);
                Assert.True(window.FindControl<ScrollViewer>("SectionScroll")?.Viewport.Height > 600);
                model.Section = SettingsSection.Toolbar;
                await UiTestSession.PumpFramesAsync(100);
                ToolbarSettingsView toolbar = window.GetVisualDescendants().OfType<ToolbarSettingsView>().Single();
                ItemsControl[] groups = toolbar.GetVisualDescendants().OfType<ItemsControl>().ToArray();
                Assert.Equal(2, groups.Length);
                Assert.Single(toolbar.GetVisualDescendants().OfType<Border>(), border => border.Height == 1);
                Assert.Equal(new ToolbarItemId[] { ToolbarItemId.Copy, ToolbarItemId.Save, ToolbarItemId.Recognize, ToolbarItemId.Cancel }, model.ActionPanelItems.Select(item => item.Id));
                Assert.All(model.DrawingPanelItems, item => Assert.False(ToolbarCatalog.IsActionPanel(item.Id)));
                Assert.DoesNotContain(model.ToolbarItems, item => item.Id == ToolbarItemId.Settings);
                ToggleSwitch lastAction = groups[0].GetVisualDescendants().OfType<ToggleSwitch>().Last();
                ToggleSwitch firstTool = groups[1].GetVisualDescendants().OfType<ToggleSwitch>().First();
                Point previousBottom = lastAction.TranslatePoint(new Point(0, lastAction.Bounds.Height), toolbar) ?? throw new InvalidOperationException();
                Point nextTop = firstTool.TranslatePoint(default, toolbar) ?? throw new InvalidOperationException();
                Assert.True(nextTop.Y - previousBottom.Y >= 20);
                UiTestSession.SaveFrame(window, "settings-toolbar-1.8.png");
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shortcut_HoldSpace_ShowsAnchoredProgressAndClearsOnlyTargetField(bool alternative)
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using HttpClient http = new();
            SettingsStore store = UiTestSession.CreateSettingsStore();
            SettingsViewModel model = new(store, new TestSettingsApplication(), new OcrModelStore(http), NullLogger<SettingsViewModel>.Instance, true);
            ShortcutBindingViewModel binding = model.Shortcuts.Single(row => row.Action == EditorAction.Save);
            ShortcutGesture originalPrimary = binding.Gesture;
            ShortcutGesture? originalAlternative = binding.AlternativeGesture;
            model.Section = SettingsSection.Shortcuts;
            model.ShortcutQuery = binding.Name;
            SettingsWindow window = new() { DataContext = model };
            try
            {
                window.Show();
                await UiTestSession.PumpFramesAsync(80);
                Button field = window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.DataContext, binding)
                    && button.Command is null && (button.Tag as string == "alternative") == alternative);
                field.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await UiTestSession.PumpFramesAsync(30);
                Assert.True(window.IsRecordingShortcut, model.ErrorMessage);
                Visual root = window.GetVisualAncestors().LastOrDefault() ?? window;
                TextBlock hint = root.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == UiText.Get("Kadr.HoldSpaceToClear"));
                Point location = hint.TranslatePoint(default, window) ?? throw new InvalidOperationException();
                Point anchor = field.TranslatePoint(new Point(0, field.Bounds.Height), window) ?? throw new InvalidOperationException();
                Assert.True(location.Y > anchor.Y);
                ProgressBar progress = root.GetVisualDescendants().OfType<ProgressBar>().Single(bar => bar.Maximum == 1);
                Assert.False(progress.IsVisible);
                Assert.True(window.IsRecordingShortcut);

                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                await UiTestSession.PumpFramesAsync(40);
                Assert.True(progress.IsVisible);
                Assert.True(progress.Value > 0 && progress.Value < 1);
                UiTestSession.SaveFrame(window, alternative ? "shortcut-clear-alternative-1.8.png" : "shortcut-clear-primary-1.8.png");
                await UiTestSession.WaitForAsync(() => alternative ? binding.AlternativeGesture is null : binding.Gesture.IsEmpty);
                Assert.Equal(string.Empty, field.Content);
                Assert.True(window.IsRecordingShortcut);
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                Assert.False(window.IsRecordingShortcut);
                Assert.Equal(alternative ? originalPrimary : ShortcutGesture.Empty, binding.Gesture);
                Assert.Equal(alternative ? null : originalAlternative, binding.AlternativeGesture);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Scroll_EndpointsAndNearEndpoints_KeepsMaskConstantWithoutConsumingLayoutSpace()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            Border content = new() { Height = 1800, Margin = SoftScrollViewer.ContentInsets, Background = Brushes.Gray };
            SoftScrollViewer scroll = new() { Content = content };
            Window window = new() { Width = 400, Height = 500, Content = scroll };
            try
            {
                window.Show();
                await UiTestSession.PumpFramesAsync(150);
                ScrollContentPresenter presenter = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
                LinearGradientBrush mask = Assert.IsType<LinearGradientBrush>(presenter.OpacityMask);
                double viewport = scroll.Viewport.Height;
                double maximum = scroll.Extent.Height - viewport;
                foreach (double offset in new double[] { 0, 4, 80, maximum - 4, maximum, 0 })
                {
                    scroll.Offset = new Vector(0, offset);
                    await UiTestSession.PumpFramesAsync(40);
                    Assert.Equal(0, mask.GradientStops[0].Color.A);
                    Assert.Equal(0, mask.GradientStops[^1].Color.A);
                    Assert.Equal(viewport, scroll.Viewport.Height);
                }
                Assert.Equal(16 / viewport, mask.GradientStops[1].Offset, 6);
                Assert.Equal(16, content.TranslatePoint(default, presenter)?.Y);
                scroll.Offset = new Vector(0, maximum);
                await UiTestSession.PumpFramesAsync(40);
                Assert.Equal(viewport - 16, content.TranslatePoint(new Point(0, content.Bounds.Height), presenter)?.Y);

            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Scroll_SettingsSectionsAtBothLimits_KeepsContentInsideOpaqueMaskRegion()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using HttpClient http = new();
            SettingsViewModel model = new(UiTestSession.CreateSettingsStore(), new TestSettingsApplication(),
                new OcrModelStore(http), NullLogger<SettingsViewModel>.Instance, true);
            SettingsWindow window = new() { DataContext = model, Height = 600 };
            try
            {
                window.Show();
                await UiTestSession.PumpFramesAsync(60);
                ScrollViewer scroll = window.FindControl<ScrollViewer>("SectionScroll") ?? throw new InvalidOperationException();
                Grid content = Assert.IsType<Grid>(scroll.Content);
                ScrollContentPresenter presenter = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
                Assert.Equal(SoftScrollViewer.ContentInsets, content.Margin);
                foreach (SettingsSection section in Enum.GetValues<SettingsSection>())
                {
                    model.Section = section;
                    scroll.Offset = default;
                    await UiTestSession.PumpFramesAsync(40);
                    Control active = content.Children.Single(control => control.IsVisible);
                    Assert.True(active.TranslatePoint(default, presenter)?.Y >= content.Margin.Top - 0.1);
                    scroll.Offset = new Vector(0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
                    await UiTestSession.PumpFramesAsync(40);
                    Assert.True(active.TranslatePoint(new Point(0, active.Bounds.Height), presenter)?.Y <= presenter.Bounds.Height - content.Margin.Bottom + 0.1);
                    LinearGradientBrush mask = Assert.IsType<LinearGradientBrush>(presenter.OpacityMask);
                    Assert.Equal(0, mask.GradientStops[0].Color.A);
                    Assert.Equal(0, mask.GradientStops[^1].Color.A);
                }
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(900)]
    [InlineData(1200)]
    public async Task Settings_FooterAtDifferentWindowWidths_StaysLeftOfScrollbar(double width)
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using HttpClient http = new();
            SettingsViewModel model = new(UiTestSession.CreateSettingsStore(), new TestSettingsApplication(),
                new OcrModelStore(http), NullLogger<SettingsViewModel>.Instance, true);
            SettingsWindow window = new() { DataContext = model, Width = width, Height = 600 };
            try
            {
                window.Show();
                await UiTestSession.PumpFramesAsync(60);
                ScrollViewer scroll = window.FindControl<ScrollViewer>("SectionScroll") ?? throw new InvalidOperationException();
                ScrollBar bar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(control => control.Orientation == Avalonia.Layout.Orientation.Vertical);
                Assert.True(bar.IsEffectivelyVisible);
                Button save = window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, model.SaveCommand));
                double right = save.TranslatePoint(new Point(save.Bounds.Width, 0), window)?.X ?? throw new InvalidOperationException();
                double scrollbarLeft = bar.TranslatePoint(default, window)?.X ?? throw new InvalidOperationException();

                Assert.True(right <= scrollbarLeft);
                Assert.True(scrollbarLeft - right >= 4);
                Assert.Equal(scroll.Viewport.Width, Assert.IsType<Grid>(save.Parent).Bounds.Width, 4);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(24)]
    public async Task Arrow_PointerPreview_RendersRoundAtBodyThickness(double thickness)
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1000, 700));
            model.Interaction.Tool = DrawingToolId.Arrow;
            model.Interaction.Thickness = thickness;
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            try
            {
                window.Show();
                await UiTestSession.PumpFramesAsync(100);
                window.MouseMove(new Point(400, 300));
                await UiTestSession.PumpFramesAsync(30);
                using SKBitmap image = UiTestSession.CapturePixels(window);
                int[] horizontal = Enumerable.Range(300, 200).Where(x => IsRed(image.GetPixel(x, 300))).ToArray();
                int[] vertical = Enumerable.Range(200, 200).Where(y => IsRed(image.GetPixel(400, y))).ToArray();
                Assert.NotEmpty(horizontal);
                Assert.Equal(horizontal.Length, vertical.Length);
                Assert.InRange(horizontal.Length, thickness * 2 - 2, thickness * 2 + 2);
                Assert.False(IsRed(image.GetPixel(400 + (int)thickness, 300 + (int)thickness)));
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    private static bool IsRed(SKColor color)
    {
        return color.Red > 230 && color.Green < 40 && color.Blue < 40;
    }
}
