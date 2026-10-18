using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

using Kadr.Desktop.Controls;
using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Ocr;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.ViewModels;
using Kadr.Desktop.Views;
using Krivodeling.Localization.Avalonia;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;
using PixelRectangle = Kadr.Desktop.Services.Geometry.PixelRectangle;

namespace Kadr.Desktop.Tests;

[Collection("Avalonia UI")]
public sealed class EditorPolishUiTests
{
    [Fact]
    public async Task Palette_OpeningAndReversing_UsesPerspectiveAndStaggeredItems()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(new PixelRectangle(250, 200, 650, 350));
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(100);
            Button color = window.FindControl<Button>("ColorButton") ?? throw new InvalidOperationException();
            PalettePanel palette = window.FindControl<PalettePanel>("Palette") ?? throw new InvalidOperationException();
            Point button = color.TranslatePoint(new Point(19, 19), window) ?? throw new InvalidOperationException();
            window.MouseDown(button, MouseButton.Left);
            window.MouseUp(button, MouseButton.Left);
            WrapPanel colors = window.FindControl<WrapPanel>("PresetColors") ?? throw new InvalidOperationException();
            await UiTestSession.WaitForAsync(() => palette.Reveal > 0 && palette.Reveal < 1
                && colors.Children.Select(child => child.Opacity).Distinct().Count() > 1);
            TransformGroup transform = Assert.IsType<TransformGroup>(palette.RenderTransform);
            Assert.NotEqual(0, Assert.IsType<Rotate3DTransform>(transform.Children[0]).AngleX);
            Assert.True(colors.Children.Select(child => child.Opacity).Distinct().Count() > 1);
            UiTestSession.SaveFrame(window, "palette-fold-1.5.png");

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await UiTestSession.PumpFramesAsync(20);
            window.MouseDown(button, MouseButton.Left);
            window.MouseUp(button, MouseButton.Left);
            await UiTestSession.WaitForAsync(() => palette.Reveal == 1);

            Assert.Equal(1, palette.Opacity);
            Assert.All(colors.Children, child => Assert.Equal(1, child.Opacity));
            Assert.Equal(1, window.FindControl<Button>("CustomColorButton")?.Opacity);
            UiTestSession.SaveFrame(window, "palette-open-1.5.png");
            Transitions? transitions = palette.Transitions;
            palette.Transitions = null;
            palette.Reveal = 0.45;
            Assert.True(colors.Children.Max(child => child.Opacity) - colors.Children.Min(child => child.Opacity) > 0.1);
            await UiTestSession.PumpFramesAsync(50);
            UiTestSession.SaveFrame(window, "palette-fold-1.5.png");
            palette.Reveal = 1;
            palette.Transitions = transitions;
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Wheel_RapidSmallSteps_AcceleratesPredictablyAndUndoCancelsWholeBurst()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(model.Document.Bounds);
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(100);
            window.KeyPress(Key.D2, RawInputModifiers.None, PhysicalKey.Digit2, "2");
            window.MouseDown(new Point(300, 250), MouseButton.Left);
            window.MouseUp(new Point(500, 250), MouseButton.Left);
            window.MouseDown(new Point(400, 250), MouseButton.Left);
            window.MouseUp(new Point(400, 250), MouseButton.Left);
            double original = Assert.Single(model.Document.Elements).Thickness;
            List<double> samples = [];
            model.Document.Changed += (sender, arguments) => samples.Add(model.Document.Elements[0].Thickness);
            ScreenshotCanvas surface = window.FindControl<ScreenshotCanvas>("Surface") ?? throw new InvalidOperationException();
            Pointer pointer = new(1, PointerType.Mouse, true);
            Action<double> wheel = delta => surface.RaiseEvent(new PointerWheelEventArgs(surface, pointer, window,
                new Point(400, 250), 0, new PointerPointProperties(), KeyModifiers.None, new Vector(0, delta)));

            for (int index = 0; index < 5; index++)
            {
                wheel(1);
            }
            await UiTestSession.PumpFramesAsync(250);

            Assert.Equal(original + 29, Assert.Single(model.Document.Elements).Thickness, 5);
            Assert.Contains(samples, value => value > original && value < original + 29);
            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            Assert.Equal(original, Assert.Single(model.Document.Elements).Thickness);
            await UiTestSession.PumpFramesAsync(250);
            Assert.Equal(original, Assert.Single(model.Document.Elements).Thickness);
            for (int index = 0; index < 3; index++)
            {
                wheel(1);
            }
            wheel(-2);
            await UiTestSession.PumpFramesAsync(250);
            Assert.Equal(original + 5.5, Assert.Single(model.Document.Elements).Thickness, 5);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(24)]
    public async Task Number_PointerPreview_MatchesPlacedCircleDiameter(double thickness)
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(model.Document.Bounds);
            model.Interaction.AutoSelectCreatedElements = false;
            model.Interaction.Thickness = thickness;
            model.Interaction.Color = 0xffff0000;
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(100);
            window.KeyPress(Key.N, RawInputModifiers.None, PhysicalKey.N, "n");
            window.MouseMove(new Point(400, 300));
            await UiTestSession.PumpFramesAsync(50);
            using SKBitmap preview = UiTestSession.CapturePixels(window);
            int[] previewEdges = Enumerable.Range(350, 100).Where(x => IsRed(preview.GetPixel(x, 300))).ToArray();

            window.MouseDown(new Point(400, 300), MouseButton.Left);
            window.MouseUp(new Point(400, 300), MouseButton.Left);
            await UiTestSession.PumpFramesAsync(50);
            using SKBitmap placed = UiTestSession.CapturePixels(window);
            int[] placedEdges = Enumerable.Range(350, 100).Where(x => IsRed(placed.GetPixel(x, 300))).ToArray();

            Assert.NotEmpty(previewEdges);
            Assert.InRange(Math.Abs((previewEdges[^1] - previewEdges[0]) - (placedEdges[^1] - placedEdges[0])), 0, 1);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Marker_PointerPreview_UsesTenfoldSizeAndStrokeOpacity()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(model.Document.Bounds);
            model.Interaction.Thickness = 20;
            model.Interaction.Color = 0xffff0000;
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(100);
            window.KeyPress(Key.D6, RawInputModifiers.None, PhysicalKey.Digit6, "6");
            window.MouseMove(new Point(400, 300));

            await UiTestSession.PumpFramesAsync(50);
            using SKBitmap preview = UiTestSession.CapturePixels(window);

            SKColor color = preview.GetPixel(400, 300);
            Assert.Equal(255, color.Red);
            Assert.InRange(color.Green, (byte)158, (byte)160);
            Assert.Equal(color.Green, color.Blue);
            Assert.Equal(color, preview.GetPixel(400, 390));
            Assert.Equal(SKColors.White, preview.GetPixel(400, 402));
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Text_EditingEdgeHover_UsesSameMoveCursorAsDrag()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(model.Document.Bounds);
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(100);
            window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
            window.MouseDown(new Point(400, 300), MouseButton.Left);
            window.MouseUp(new Point(400, 300), MouseButton.Left);
            TextBox entry = window.FindControl<TextBox>("TextEntry") ?? throw new InvalidOperationException();
            entry.Text = "Text edge";
            window.MouseMove(new Point(398, 300));

            ScreenshotCanvas surface = window.FindControl<ScreenshotCanvas>("Surface") ?? throw new InvalidOperationException();
            Assert.Equal("SizeAll", surface.Cursor?.ToString());
            window.MouseDown(new Point(398, 300), MouseButton.Left);
            window.MouseMove(new Point(378, 300));
            window.MouseUp(new Point(378, 300), MouseButton.Left);
            Assert.Equal(380, Assert.Single(model.Document.Elements).Points[0].X);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Language_ChangedWithoutSaving_AppliesAndPersistsImmediately()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "Kadr-live-language-" + Guid.NewGuid().ToString("N"));
            using LocalizationService localization = new(new DirectoryLocalizationFileStore(directory), UiText.Catalog, NullLogger<LocalizationService>.Instance);
            using HttpClient http = new();
            SettingsStore store = new(NullLogger<SettingsStore>.Instance, Path.Combine(directory, "settings.json"));
            TestSettingsApplication application = new();
            SettingsViewModel model = new(store, application, new OcrModelStore(http), NullLogger<SettingsViewModel>.Instance, true, localization);
            SettingsWindow window = new() { DataContext = model };
            try
            {
                window.Show();
                localization.Select(LocalizationConstants.RussianId);

                model.LocalizationId = LocalizationConstants.EnglishId;

                Assert.Equal("Kadr", window.Title);
                Assert.Null(application.AppliedSettings);
                if (model.ApplyLocalizationCommand.ExecutionTask is Task saving)
                {
                    await saving;
                }
                Assert.Equal(LocalizationConstants.EnglishId, store.Current.LocalizationId);
                window.Close();
            }
            finally
            {
                localization.Select(LocalizationConstants.RussianId);
                window.Close();
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            return true;
        }, CancellationToken.None);
    }

    private static bool IsRed(SKColor color)
    {
        return color.Red > 240 && color.Green < 20 && color.Blue < 20;
    }
}
