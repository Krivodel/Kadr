using Kadr.Desktop.Services.Export;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Ocr;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task Save_CustomShortcutAndClipboardFormat_RoundTripsAndControlsImageCopy()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-settings-ui-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        using HttpClient http = new();
        SettingsStore store = new(NullLogger<SettingsStore>.Instance, path);
        TestSettingsApplication application = new();
        SettingsViewModel model = new(store, application, new OcrModelStore(http, Path.Combine(AppContext.BaseDirectory, "tessdata")),
            NullLogger<SettingsViewModel>.Instance, true);
        model.AutoSelectCreatedElements = true;
        model.ExitImmediately = true;
        model.ShowCursorWhileDrawing = true;
        model.OutsideSelectionIndex = (int)OutsideSelectionBehavior.Reselect;
        model.CopyFormatIndex = (int)ScreenshotFormat.Jpeg;
        model.ToolbarItems.Single(item => item.Id == ToolbarItemId.Arrow).IsVisible = false;
        Assert.DoesNotContain(model.ToolbarItems, item => item.Id == ToolbarItemId.Settings);
        model.Shortcuts.Single(row => row.Action == EditorAction.Arrow).Gesture = new ShortcutGesture(0x71, ShortcutModifiers.None);
        model.Shortcuts.Single(row => row.Action == EditorAction.Delete).Gesture = new ShortcutGesture(0x72, ShortcutModifiers.None);
        model.Shortcuts.Single(row => row.Action == EditorAction.Delete).AlternativeGesture = new ShortcutGesture(4, ShortcutModifiers.Control);

        try
        {
            await store.UpdateAsync(current => current with
            {
                ShowCursor = false, Color = 0xff00ff00, Thickness = 37, ColorPickerTab = ColorPickerTab.Hsl,
                HiddenToolbarItems = new ToolbarItemId[] { ToolbarItemId.Settings }
            }, CancellationToken.None);
            await model.SaveCommand.ExecuteAsync(null);
            AppSettings? applied = application.AppliedSettings;
            Assert.NotNull(applied);
            Assert.Null(model.ErrorMessage);
            await store.SaveAsync(applied, CancellationToken.None);
            SettingsStore reloaded = new(NullLogger<SettingsStore>.Instance, path);
            await reloaded.LoadAsync(CancellationToken.None);
            EditorTestServices services = new();
            using EditorViewModel editor = EditorViewModelTests.Create(services, settings: reloaded);

            await editor.CopyCommand.ExecuteAsync(null);

            Assert.Equal(ScreenshotFormat.Jpeg, services.CopiedFormat);
            Assert.Equal(ColorPickerTab.Hsl, reloaded.Current.ColorPickerTab);
            Assert.Equal(0x71, ShortcutCatalog.Get(reloaded.Current, EditorAction.Arrow).Key);
            Assert.Equal(0x72, ShortcutCatalog.Get(reloaded.Current, EditorAction.Delete).Key);
            Assert.Equal(new ShortcutGesture(4, ShortcutModifiers.Control), ShortcutCatalog.GetAlternative(reloaded.Current, EditorAction.Delete));
            Assert.False(reloaded.Current.ShowCursor);
            Assert.Equal(0xff00ff00u, reloaded.Current.Color);
            Assert.Equal(37, reloaded.Current.Thickness);
            Assert.True(reloaded.Current.AutoSelectCreatedElements);
            Assert.True(reloaded.Current.ExitImmediately);
            Assert.False(reloaded.Current.HideCursorWhileDrawing);
            Assert.Equal(OutsideSelectionBehavior.Reselect, reloaded.Current.OutsideSelectionBehavior);
            Assert.Equal(new ToolbarItemId[] { ToolbarItemId.Arrow }, reloaded.Current.HiddenToolbarItems);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }
}
