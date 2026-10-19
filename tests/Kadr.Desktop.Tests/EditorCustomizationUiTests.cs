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
public sealed class EditorCustomizationUiTests
{
    [Fact]
    public async Task Arrow_MiddleHandleDrag_RendersCurveAndThreeControls()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800));
            model.Document.SetSelection(new PixelRectangle(150, 100, 900, 550));
            model.Interaction.AutoSelectCreatedElements = true;
            model.Interaction.Color = 0xffff0000;
            model.Interaction.Thickness = 18;
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(100);
            window.KeyPress(Key.D1, RawInputModifiers.None, PhysicalKey.Digit1, "1");
            window.MouseDown(new Point(300, 450), MouseButton.Left);
            window.MouseUp(new Point(900, 450), MouseButton.Left);

            window.MouseDown(new Point(600, 450), MouseButton.Left);
            window.MouseMove(new Point(600, 250));
            window.MouseUp(new Point(600, 250), MouseButton.Left);

            DrawingElement element = Assert.Single(model.Document.Elements);
            Assert.Equal(250, element.Bend?.Y);
            Assert.Equal(3, ElementEditing.GetControls(element).Count);
            await UiTestSession.PumpFramesAsync(100);
            UiTestSession.SaveFrame(window, "bezier-arrow-1.4.png");
            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            Assert.Null(Assert.Single(model.Document.Elements).Bend);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(RawInputModifiers.Control)]
    [InlineData(RawInputModifiers.Shift)]
    [InlineData(RawInputModifiers.Control | RawInputModifiers.Shift)]
    public async Task Draw_Modifier_ConstrainsLineWithoutCopying(RawInputModifiers modifiers)
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            EditorTestServices services = new();
            using EditorViewModel model = EditorViewModelTests.Create(services, TestDocuments.Create(1280, 800));
            model.Document.SetSelection(model.Document.Bounds);
            EditorWindow window = UiTestSession.CreateEditorWindow(model);
            window.Show();
            await UiTestSession.PumpFramesAsync(100);
            window.KeyPress(Key.D2, RawInputModifiers.None, PhysicalKey.Digit2, "2");

            window.MouseDown(new Point(200, 200), MouseButton.Left, modifiers);
            window.MouseMove(new Point(400, 215), modifiers);
            window.MouseUp(new Point(400, 215), MouseButton.Left, modifiers);

            Assert.Equal(200, Assert.Single(model.Document.Elements).Points[^1].Y, 6);
            Assert.Equal(0, services.ClipboardWrites);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SelectAll_CustomShortcut_ReplacesDefaultAndPreservesTextSelectAll()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "Kadr-select-all-" + Guid.NewGuid().ToString("N"));
            SettingsStore store = new(NullLogger<SettingsStore>.Instance, Path.Combine(directory, "settings.json"));
            try
            {
                await store.SaveAsync(new AppSettings
                {
                    EditorShortcuts = new Dictionary<EditorAction, ShortcutGesture> { [EditorAction.SelectAll] = new ShortcutGesture(0x71, ShortcutModifiers.None) }
                }, CancellationToken.None);
                using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(1280, 800), store);
                PixelRectangle selection = new(100, 100, 500, 400);
                model.Document.SetSelection(selection);
                EditorWindow window = UiTestSession.CreateEditorWindow(model, store);
                window.Show();
                await UiTestSession.PumpFramesAsync(100);
                window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
                Assert.Equal(selection, model.Document.Selection);

                window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);

                Assert.Equal(model.Document.Bounds, model.Document.Selection);
                window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
                window.MouseDown(new Point(200, 200), MouseButton.Left);
                window.MouseUp(new Point(200, 200), MouseButton.Left);
                TextBox text = window.FindControl<TextBox>("TextEntry") ?? throw new InvalidOperationException();
                text.Text = "Editable text";
                window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
                Assert.Equal("Editable text", text.SelectedText);
                window.Close();
            }
            finally
            {
                Directory.Delete(directory, true);
            }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settings_CustomExitShortcut_ClosesWindow(bool mouse)
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "Kadr-settings-exit-" + Guid.NewGuid().ToString("N"));
            SettingsStore store = new(NullLogger<SettingsStore>.Instance, Path.Combine(directory, "settings.json"));
            try
            {
                await store.SaveAsync(new AppSettings
                {
                    AlternativeShortcuts = new Dictionary<EditorAction, ShortcutGesture?> { [EditorAction.Cancel] = new ShortcutGesture(mouse ? 4 : 0x72, ShortcutModifiers.Control) }
                }, CancellationToken.None);
                using HttpClient http = new();
                SettingsViewModel model = new(store, new TestSettingsApplication(), NullLogger<SettingsViewModel>.Instance, true);
                SettingsWindow window = new() { DataContext = model };
                window.Show();
                await UiTestSession.PumpFramesAsync(100);

                if (mouse)
                {
                    window.MouseDown(new Point(30, 30), MouseButton.Middle, RawInputModifiers.Control);
                }
                else
                {
                    window.KeyPress(Key.F3, RawInputModifiers.Control, PhysicalKey.F3, null);
                }

                Assert.False(window.IsVisible);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Localization_ChangesLanguage_UpdatesSettingsAndLoadsCustomTranslationWithFallback()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "Kadr-localization-" + Guid.NewGuid().ToString("N"));
            using LocalizationService localization = new(new DirectoryLocalizationFileStore(directory), UiText.Catalog, NullLogger<LocalizationService>.Instance);
            using HttpClient http = new();
            TestSettingsApplication application = new();
            SettingsViewModel model = new(new SettingsStore(NullLogger<SettingsStore>.Instance, Path.Combine(directory, "settings.json")), application,
                NullLogger<SettingsViewModel>.Instance, true, localization) { Section = SettingsSection.Editor };
            SettingsWindow window = new() { DataContext = model };
            try
            {
                await localization.RefreshAvailableLocalizationsAsync(CancellationToken.None);
                localization.Select(LocalizationConstants.RussianId);
                model.LocalizationId = LocalizationConstants.RussianId;
                window.Show();
                await UiTestSession.PumpFramesAsync(100);
                EditorSettingsView editor = window.GetVisualDescendants().OfType<EditorSettingsView>().Single();
                Border card = Assert.Single(editor.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("card"));
                Assert.InRange(card.Bounds.Height, 100, 400);
                Assert.Equal(3, editor.GetVisualDescendants().OfType<ToggleSwitch>().Count());
                Assert.DoesNotContain(editor.GetVisualDescendants().OfType<TextBlock>(), text => text.Classes.Contains("card-title"));
                Assert.Empty(editor.GetVisualDescendants().OfType<NumericUpDown>());
                UiTestSession.SaveFrame(window, "settings-editor-1.4-ru.png");

                model.Section = SettingsSection.General;
                await UiTestSession.PumpFramesAsync(80);
                UiTestSession.SaveFrame(window, "settings-general-1.4-ru.png");
                ComboBox languages = window.GetVisualDescendants().OfType<ComboBox>().Single(box => box.Name == "InterfaceLanguageBox");
                Assert.Equal(LocalizationConstants.RussianId, languages.SelectedValue);
                languages.SelectedItem = model.InterfaceLanguages.Single(language => language.Id == LocalizationConstants.EnglishId);
                Assert.Equal(LocalizationConstants.EnglishId, model.LocalizationId);
                await model.SaveCommand.ExecuteAsync(null);
                Assert.Equal(LocalizationConstants.EnglishId, application.AppliedSettings?.LocalizationId);
                model.Section = SettingsSection.Editor;
                await UiTestSession.PumpFramesAsync(80);

                Assert.Equal("Kadr", window.Title);
                Assert.Equal("Drawing", model.SectionTitle);
                Assert.Equal("Arrow", model.Shortcuts.Single(shortcut => shortcut.Action == EditorAction.Arrow).Name);
                Assert.Contains(editor.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Hide the cursor while drawing");
                UiTestSession.SaveFrame(window, "settings-editor-1.4-en.png");
                await File.WriteAllTextAsync(Path.Combine(directory, "Custom.json"), """{"schemaVersion":1,"culture":"de-DE","strings":{"Kadr":{"Editor":"Editor DE"}}}""");
                await localization.RefreshAvailableLocalizationsAsync(CancellationToken.None);
                localization.Select("Custom");
                Assert.Equal("Editor DE", model.SectionTitle);
                Assert.Equal("Arrow", UiText.Get("Kadr.Arrow"));
                Assert.True(File.Exists(Path.Combine(directory, "_template.json")));
            }
            finally
            {
                localization.Select(LocalizationConstants.RussianId);
                window.Close();
                Directory.Delete(directory, true);
            }
            return true;
        }, CancellationToken.None);
    }
}
