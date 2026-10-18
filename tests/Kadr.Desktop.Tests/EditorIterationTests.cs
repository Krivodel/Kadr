using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

using Kadr.Desktop.Controls;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.ViewModels;
using Kadr.Desktop.Views;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kadr.Desktop.Tests;

[Collection("Avalonia UI")]
public sealed class EditorIterationTests
{
    [Fact]
    public async Task Editor_EndpointOverActiveTool_DragsControlAndMiddleDeselectsTool()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Interaction.AutoSelectCreatedElements = true;
            model.Document.SetSelection(new PixelRectangle(300, 200, 600, 350));
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(120);
            window.KeyPress(Key.D1, RawInputModifiers.None, PhysicalKey.Digit1, "1");
            window.MouseDown(new Point(400, 300), MouseButton.Left);
            window.MouseMove(new Point(600, 300), RawInputModifiers.LeftMouseButton);
            Assert.True(model.Interaction.IsDrawing);
            window.MouseUp(new Point(600, 300), MouseButton.Left);
            window.MouseDown(new Point(600, 300), MouseButton.Left);
            window.MouseMove(new Point(650, 350), RawInputModifiers.LeftMouseButton);
            Assert.False(model.Interaction.IsDrawing);
            window.MouseUp(new Point(650, 350), MouseButton.Left);

            Assert.Equal(new Services.Geometry.PixelPoint(650, 350), Assert.Single(model.Document.Elements).Points[^1]);
            window.MouseDown(new Point(700, 400), MouseButton.Middle);
            window.MouseUp(new Point(700, 400), MouseButton.Middle);
            Assert.Null(model.Interaction.Tool);
            Assert.Null(model.Interaction.SelectedId);
            window.KeyPress(Key.D2, RawInputModifiers.None, PhysicalKey.Digit2, "2");
            window.MouseDown(new Point(400, 400), MouseButton.Left);
            window.MouseMove(new Point(550, 400), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(550, 400), MouseButton.Left);
            Assert.NotNull(model.Interaction.SelectedId);
            window.KeyPress(Key.OemTilde, RawInputModifiers.None, PhysicalKey.Backquote, "`");
            Assert.Null(model.Interaction.Tool);
            Assert.Null(model.Interaction.SelectedId);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Text_InlineTyping_UpdatesDocumentBeforeCommitWithoutPanel()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(new PixelRectangle(300, 200, 600, 350));
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(100);
            window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
            window.MouseDown(new Point(400, 300), MouseButton.Left);
            window.MouseUp(new Point(400, 300), MouseButton.Left);
            TextBox entry = window.FindControl<TextBox>("TextEntry") ?? throw new InvalidOperationException();

            entry.Text = "Inline text\nSecond line";
            await UiTestSession.PumpFramesAsync(80);

            Assert.Equal(entry.Text, Assert.Single(model.Document.Elements).Text);
            Assert.Equal(Brushes.Transparent, entry.Background);
            Assert.Equal(new Thickness(0), entry.BorderThickness);
            UiTestSession.SaveFrame(window, "inline-text-1.2.png");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(entry.IsVisible);
            Assert.Equal(DrawingToolId.Text, model.Interaction.Tool);
            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            Assert.Empty(model.Document.Elements);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Palette_HoverSwatch_PreservesColorAndUsesBorder()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(new PixelRectangle(300, 200, 600, 350));
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(120);
            Button color = window.FindControl<Button>("ColorButton") ?? throw new InvalidOperationException();
            Point position = color.TranslatePoint(new Point(19, 19), window) ?? throw new InvalidOperationException();
            window.MouseDown(position, MouseButton.Left);
            window.MouseUp(position, MouseButton.Left);
            await UiTestSession.PumpFramesAsync(120);
            WrapPanel presets = window.FindControl<WrapPanel>("PresetColors") ?? throw new InvalidOperationException();
            Assert.Equal(9, presets.Children.Count);
            Assert.Contains(presets.Children.OfType<Button>(), button => button.Tag is uint color && color == 0xffff0000);
            Assert.Contains(presets.Children.OfType<Button>(), button => button.Tag is uint color && color == 0xff00ff00);
            Button purple = (Button)presets.Children[6];
            Assert.Equal(0xffa000ffu, purple.Tag);
            Border hover = purple.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "HoverLayer");
            Color before = Assert.IsAssignableFrom<ISolidColorBrush>(purple.Background).Color;

            window.MouseMove(purple.TranslatePoint(new Point(18, 16), window) ?? throw new InvalidOperationException());
            await UiTestSession.PumpFramesAsync(90);

            Assert.Equal(before, Assert.IsAssignableFrom<ISolidColorBrush>(purple.Background).Color);
            Assert.Equal(Color.Parse("#E7EDF6"), Assert.IsAssignableFrom<ISolidColorBrush>(hover.BorderBrush).Color);
            Assert.Equal(1, hover.Opacity);
            UiTestSession.SaveFrame(window, "palette-1.2.png");
            window.Close();
            ColorPickerWindow picker = new(0xffec3e54);
            picker.Show();
            await UiTestSession.PumpFramesAsync(80);
            UiTestSession.SaveFrame(picker, "unity-picker-1.2.png");
            picker.Close();
            return true;
        }, CancellationToken.None);
    }


}
