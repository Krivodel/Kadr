using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

using Kadr.Desktop.Controls;
using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Ocr;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.ViewModels;
using Kadr.Desktop.Views;
using Kadr.Desktop.Views.Settings;
using Krivodeling.Localization.Avalonia;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using PixelRectangle = Kadr.Desktop.Services.Geometry.PixelRectangle;

namespace Kadr.Desktop.Tests;

[Collection("Avalonia UI")]
public sealed class EditorRefinementsUiTests
{
    [Fact]
    public async Task Language_SwitchWithSelectedOptions_UpdatesVisibleComboTextInBothDirections()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "Kadr-combo-language-" + Guid.NewGuid().ToString("N"));
            using LocalizationService localization = new(new DirectoryLocalizationFileStore(directory), UiText.Catalog, NullLogger<LocalizationService>.Instance);
            using HttpClient http = new();
            SettingsStore store = new(NullLogger<SettingsStore>.Instance, Path.Combine(directory, "settings.json"));
            localization.Select(LocalizationConstants.RussianId);
            SettingsViewModel model = new(store, new TestSettingsApplication(), new OcrModelStore(http), NullLogger<SettingsViewModel>.Instance, true, localization);
            SettingsWindow window = new() { DataContext = model };
            try
            {
                window.Show();
                await UiTestSession.PumpFramesAsync(80);
                GeneralSettingsView general = window.GetVisualDescendants().OfType<GeneralSettingsView>().Single();
                ComboBox mode = general.FindControl<ComboBox>("CaptureModeBox") ?? throw new InvalidOperationException();
                ComboBox format = general.FindControl<ComboBox>("CopyFormatBox") ?? throw new InvalidOperationException();
                foreach (string id in new string[] { LocalizationConstants.EnglishId, LocalizationConstants.RussianId })
                {
                    model.LocalizationId = id;
                    if (model.ApplyLocalizationCommand.ExecutionTask is Task saving)
                    {
                        await saving;
                    }
                    await UiTestSession.PumpFramesAsync(50);

                    Assert.Contains(mode.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == UiText.Get("Kadr.OnDemandMode"));
                    Assert.Contains(format.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == UiText.Get("Kadr.PngFormat"));
                    Assert.Equal(1, mode.SelectedIndex);
                    Assert.Equal(0, format.SelectedIndex);
                    UiTestSession.SaveFrame(window, id == LocalizationConstants.EnglishId ? "settings-general-1.6-en.png" : "settings-general-1.6-ru.png");
                }
                model.Section = SettingsSection.Toolbar;
                await UiTestSession.PumpFramesAsync(50);
                Assert.Equal(Enum.GetValues<ToolbarItemId>().Length - 1, model.ToolbarItems.Count);
                Assert.DoesNotContain(model.ToolbarItems, item => item.Id == ToolbarItemId.Settings);
                UiTestSession.SaveFrame(window, "settings-toolbar-1.6.png");
            }
            finally
            {
                window.Close();
                localization.Select(LocalizationConstants.RussianId);
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Toolbar_LiveVisibility_HidesButtonsAndEmptyGroupsWithoutDisablingShortcuts()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(new PixelRectangle(250, 200, 650, 350));
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(200);
            Border actions = window.FindControl<Border>("Actions") ?? throw new InvalidOperationException();
            Button[] actionButtons = actions.GetVisualDescendants().OfType<Button>().Where(button => button.IsVisible).ToArray();
            Assert.Equal(new object[] { model.CopyCommand, model.SaveCommand, model.RecognizeCommand, model.SettingsCommand, model.CancelCommand },
                actionButtons.Select(button => (object?)button.Command));
            Button color = window.FindControl<Button>("ColorButton") ?? throw new InvalidOperationException();
            color.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            model.ApplySettings(new AppSettings { HiddenToolbarItems = new ToolbarItemId[] { ToolbarItemId.Color, ToolbarItemId.Copy, ToolbarItemId.Arrow } });
            await UiTestSession.PumpFramesAsync(250);

            Assert.False(color.IsVisible);
            Assert.False(actionButtons[0].IsVisible);
            Assert.DoesNotContain(model.DrawingTools, tool => tool.Id == DrawingToolId.Arrow);
            Assert.False(window.FindControl<PalettePanel>("Palette")?.IsHitTestVisible);
            window.KeyPress(Key.D1, RawInputModifiers.None, PhysicalKey.Digit1, "1");
            Assert.Equal(DrawingToolId.Arrow, model.Interaction.Tool);
            UiTestSession.SaveFrame(window, "toolbar-customized-1.6.png");
            model.ApplySettings(new AppSettings { HiddenToolbarItems = Enum.GetValues<ToolbarItemId>() });
            await UiTestSession.PumpFramesAsync(250);
            Assert.Equal(1, actions.Opacity);
            Assert.True(actions.IsHitTestVisible);
            Assert.Single(actionButtons, button => button.IsVisible);
            Assert.True(actionButtons[3].IsVisible);
            bool settingsOpened = false;
            model.SettingsRequested += (sender, arguments) => settingsOpened = true;
            Point settingsButton = actionButtons[3].TranslatePoint(new Point(19, 19), window) ?? throw new InvalidOperationException();
            window.MouseDown(settingsButton, MouseButton.Left);
            window.MouseUp(settingsButton, MouseButton.Left);
            Assert.True(settingsOpened);
            Assert.Equal(0, window.FindControl<Border>("ToolsPanel")?.Opacity);
            Assert.Empty(model.DrawingTools);
            Assert.Empty(model.LabelTools);
            model.ApplySettings(new AppSettings());
            await UiTestSession.PumpFramesAsync(250);
            Assert.Equal(1, actions.Opacity);
            Assert.All(actionButtons, button => Assert.True(button.IsVisible));
            Assert.Equal(9, model.VisibleToolCount);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Selection_ReselectOutside_UsesCrossCursorAndKeepsResizeHandles()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(new PixelRectangle(250, 200, 650, 350));
            model.ApplySettings(new AppSettings { OutsideSelectionBehavior = OutsideSelectionBehavior.Reselect });
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(100);
            ScreenshotCanvas surface = window.FindControl<ScreenshotCanvas>("Surface") ?? throw new InvalidOperationException();

            window.MouseMove(new Point(80, 80));
            Assert.Equal("Cross", surface.Cursor?.ToString());
            window.MouseMove(new Point(250, 350));
            Assert.Equal("SizeWestEast", surface.Cursor?.ToString());
            window.MouseMove(new Point(400, 350));
            Assert.Equal("Arrow", surface.Cursor?.ToString());
            window.Close();
            return true;
        }, CancellationToken.None);
    }
}
