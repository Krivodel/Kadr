using Kadr.Desktop.Services.Settings;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class ShortcutCatalogTests
{
    [Fact]
    public void Alternatives_Defaults_SaveSelectAndDeleteHaveBindingsAndCanClear()
    {
        AppSettings settings = new();

        EditorAction[] actions = ShortcutCatalog.Definitions.Where(definition => ShortcutCatalog.GetAlternative(settings, definition.Action) is not null)
            .Select(definition => definition.Action).ToArray();

        Assert.Equal(new EditorAction[] { EditorAction.Save, EditorAction.Redo, EditorAction.Select, EditorAction.Delete }, actions);
        Assert.Equal(new ShortcutGesture(4, ShortcutModifiers.None), ShortcutCatalog.GetAlternative(settings, EditorAction.Select));
        Assert.Equal(0xc0, ShortcutCatalog.Get(settings, EditorAction.Select).Key);
        Assert.Null(ShortcutCatalog.GetAlternative(settings with
        {
            AlternativeShortcuts = new Dictionary<EditorAction, ShortcutGesture?> { [EditorAction.Select] = null }
        }, EditorAction.Select));
    }

    [Fact]
    public void Policy_AlternativeConflictsWithPrimary_ReportsConflict()
    {
        AppSettings settings = new()
        {
            AlternativeShortcuts = new Dictionary<EditorAction, ShortcutGesture?>
            {
                [EditorAction.Arrow] = new ShortcutGesture(0x42, ShortcutModifiers.None)
            }
        };

        string? error = ShortcutPolicy.GetError(settings);

        Assert.NotNull(error);
    }

    [Fact]
    public void Defaults_AllEditorActions_AreUniqueAndUseRequestedKeys()
    {
        AppSettings settings = new();

        Assert.Null(ShortcutPolicy.GetError(settings));
        Assert.Equal(0x43, ShortcutCatalog.Get(settings, EditorAction.Cursor).Key);
        Assert.Equal(new ShortcutGesture(0x12, ShortcutModifiers.None), ShortcutCatalog.Get(settings, EditorAction.Preview));
        Assert.Equal(0x31, ShortcutCatalog.Get(settings, EditorAction.Arrow).Key);
        Assert.Equal(new ShortcutGesture(0x54, ShortcutModifiers.Control), ShortcutCatalog.Get(settings, EditorAction.Recognize));
    }

    [Fact]
    public void Apply_CustomToolShortcut_PreservesUnrelatedPreferencesAndRejectsConflicts()
    {
        AppSettings settings = new()
        {
            ShowCursor = false,
            Thickness = 19
        };
        Dictionary<EditorAction, ShortcutGesture> shortcuts = ShortcutCatalog.Definitions.ToDictionary(
            definition => definition.Action, definition => ShortcutCatalog.Get(settings, definition.Action));
        shortcuts[EditorAction.Arrow] = new ShortcutGesture(0x70, ShortcutModifiers.None);

        AppSettings changed = ShortcutCatalog.Apply(settings, shortcuts);

        Assert.Null(ShortcutPolicy.GetError(changed));
        Assert.False(changed.ShowCursor);
        Assert.Equal(19, changed.Thickness);
        Assert.Equal(0x70, ShortcutCatalog.Get(changed, EditorAction.Arrow).Key);
        Assert.NotNull(ShortcutPolicy.GetError(changed with
        {
            EditorShortcuts = new Dictionary<EditorAction, ShortcutGesture> { [EditorAction.Arrow] = settings.CaptureShortcut }
        }));
    }
}
