using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Ocr;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.ViewModels;
using Kadr.Desktop.Views;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Kadr.Desktop.Tests;

[Collection("Avalonia UI")]
public sealed class WindowCompositionTests
{
    [Fact]
    public async Task EditorWindow_DragToLastPhysicalPixel_SelectsEntireMonitorAndUsesCrosshair()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            DrawingDocument document = TestDocuments.Create(2560, 1440);
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), document);
            document.SetSelection(default);
            EditorWindow window = new(model, document.Desktop.Monitors[0], UiTestSession.CreateSettingsStore());
            window.Show();
            await UiTestSession.PumpFramesAsync(150);
            Controls.ScreenshotCanvas? surface = window.FindControl<Controls.ScreenshotCanvas>("Surface");
            Assert.NotNull(surface);
            Assert.Equal("Cross", surface.Cursor?.ToString());

            window.MouseDown(new Avalonia.Point(0, 0), MouseButton.Left);
            window.MouseMove(new Avalonia.Point(2559, 1439), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Avalonia.Point(2559, 1439), MouseButton.Left);

            Assert.Equal(new PixelRectangle(0, 0, 2560, 1440), document.Selection);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EditorWindow_PreviewAndClosing_AnimateDimWithoutChangingDocument()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            DrawingDocument document = TestDocuments.Create(1280, 800);
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), document);
            document.SetSelection(new PixelRectangle(500, 200, 300, 200));
            EditorWindow window = new(model, document.Desktop.Monitors[0], UiTestSession.CreateSettingsStore());
            window.Show();
            await UiTestSession.PumpFramesAsync(150);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Controls.ScreenshotCanvas? surface = window.FindControl<Controls.ScreenshotCanvas>("Surface");
            Assert.NotNull(surface);
            Assert.Equal(1, surface.DimAmount);
            long revision = document.Revision;
            IReadOnlyList<double> preview = await UiTestSession.ObserveTransitionAsync(surface,
                Controls.ScreenshotCanvas.PreviewAmountProperty, 1, () => surface.PreviewAmount = 1);
            Assert.Contains(preview, value => (value > 0) && (value < 1));
            Assert.Equal(1, surface.PreviewAmount);

            Task closing = Task.CompletedTask;
            IReadOnlyList<double> fadingOut = await UiTestSession.ObserveTransitionAsync(surface,
                Controls.ScreenshotCanvas.DimAmountProperty, 0, () =>
                {
                    closing = window.FadeOutAsync();
                    model.IsPreview = true;
                    model.IsPreview = false;
                    Assert.True(window.IsVisible);
                });
            Assert.Contains(fadingOut, value => (value > 0) && (value < 1));
            await closing;

            Assert.False(window.FindControl<Border>("ToolsPanel")?.IsHitTestVisible);
            Assert.Equal(revision, document.Revision);
            Assert.Equal(new PixelRectangle(500, 200, 300, 200), document.Selection);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ColorPickerWindow_ChangingHueOnWhite_PreservesHueForSubsequentSaturationChange()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(() =>
        {
            ColorPickerWindow window = new(0xffffffff, Services.Settings.ColorPickerTab.Hsl);
            window.Show();
            Controls.ColorSlider hue = window.FindControl<Controls.ColorSlider>("HslHueSlider") ?? throw new InvalidOperationException("Hue slider is missing.");

            hue.Value = 240;

            Controls.SaturationValueBox? spectrum = window.FindControl<Controls.SaturationValueBox>("Spectrum");
            Assert.Equal(240, hue.Value);
            Assert.Equal(240, spectrum?.Hue);
            Assert.Equal(255, window.FindControl<NumericUpDown>("RedInput")?.Value);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SettingsWindow_InitialSettings_RendersCaptureAndLanguageOptions()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(() =>
        {
            using HttpClient http = new();
            SettingsStore store = UiTestSession.CreateSettingsStore();
            SettingsViewModel model = new(store, new TestSettingsApplication(), new OcrModelStore(http),
                NullLogger<SettingsViewModel>.Instance, false);
            SettingsWindow window = new()
            {
                DataContext = model
            };
            window.Show();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
            Directory.CreateDirectory(directory);
            frame?.Save(Path.Combine(directory, "settings-preview.png"), PngBitmapEncoderOptions.Default);

            Assert.NotNull(frame);
            Assert.Equal(2, model.Languages.Count(language => language.IsSelected));
            Assert.Equal("PrintScreen", model.Shortcuts.Single(row => row.Action == EditorAction.Capture).DisplayName);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<CheckBox>(),
                checkbox => checkbox.Content is string content && content.Contains("Системный курсор", StringComparison.OrdinalIgnoreCase));
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EditorWindow_WithSelection_RendersPreviewMatchingExportAndKeepsToolsWithinViewport()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            DrawingDocument document = TestDocuments.Create(1280, 800);
            document.SetElements(System.Collections.Immutable.ImmutableArray.Create(
                TestDocuments.Element(DrawingToolId.Arrow, 200, 220, 530, 350),
                TestDocuments.Element(DrawingToolId.Number, 560, 280, 560, 280)));
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), document);
            model.Document.SetSelection(new PixelRectangle(150, 180, 600, 360));
            SettingsStore store = UiTestSession.CreateSettingsStore();
            EditorWindow window = new(model, document.Desktop.Monitors[0], store);
            window.Show();
            await UiTestSession.PumpFramesAsync(150);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
            Directory.CreateDirectory(directory);
            frame?.Save(Path.Combine(directory, "editor-preview.png"), PngBitmapEncoderOptions.Default);

            Border? tools = window.FindControl<Border>("ToolsPanel");
            Assert.NotNull(frame);
            Assert.NotNull(tools);
            using MemoryStream pixels = new();
            frame.Save(pixels, PngBitmapEncoderOptions.Default);
            using SKBitmap preview = SKBitmap.Decode(pixels.ToArray());
            using SKBitmap export = model.Renderer.Export();
            int annotationSamples = 0;

            for (int y = 3; y < export.Height - 3; y += 7)
            {
                for (int x = 3; x < export.Width - 3; x += 7)
                {
                    SKColor expected = export.GetPixel(x, y);
                    Assert.Equal(expected, preview.GetPixel(x + 150, y + 180));
                    annotationSamples += expected != SKColors.White ? 1 : 0;
                }
            }

            Assert.True(annotationSamples > 5);
            Assert.True(tools.IsVisible);
            Assert.True(Canvas.GetLeft(tools) >= 0);
            Assert.True(Canvas.GetLeft(tools) + tools.Bounds.Width <= window.ClientSize.Width);
            Assert.NotEmpty(tools.GetVisualDescendants().OfType<Button>());
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ColorPickerWindow_InitialColor_RendersSpectrumAndRgbaInputs()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(() =>
        {
            ColorPickerWindow window = new(0xff4385f5);
            window.Show();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts"));
            Directory.CreateDirectory(directory);
            frame?.Save(Path.Combine(directory, "color-picker-preview.png"), PngBitmapEncoderOptions.Default);

            Assert.NotNull(frame);
            Assert.Equal(67, window.FindControl<NumericUpDown>("RedInput")?.Value);
            Assert.Equal(255, window.FindControl<NumericUpDown>("AlphaInput")?.Value);
            window.Close();
            return true;
        }, CancellationToken.None);
    }
}
