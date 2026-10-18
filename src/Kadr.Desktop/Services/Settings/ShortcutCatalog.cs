using Kadr.Desktop.Services.Editing;

namespace Kadr.Desktop.Services.Settings;

public static class ShortcutCatalog
{
    public static IReadOnlyList<ShortcutDefinition> Definitions
    {
        get;
    } = new ShortcutDefinition[]
    {
        new(EditorAction.Capture, "Kadr.Capture", new ShortcutGesture(0x2c, ShortcutModifiers.None)),
        new(EditorAction.Arrow, "Kadr.Arrow", new ShortcutGesture(0x31, ShortcutModifiers.None), DrawingToolId.Arrow),
        new(EditorAction.Line, "Kadr.Line", new ShortcutGesture(0x32, ShortcutModifiers.None), DrawingToolId.Line),
        new(EditorAction.Rectangle, "Kadr.Rectangle", new ShortcutGesture(0x33, ShortcutModifiers.None), DrawingToolId.Rectangle),
        new(EditorAction.Ellipse, "Kadr.Ellipse", new ShortcutGesture(0x34, ShortcutModifiers.None), DrawingToolId.Ellipse),
        new(EditorAction.Pencil, "Kadr.Pencil", new ShortcutGesture(0x35, ShortcutModifiers.None), DrawingToolId.Pencil),
        new(EditorAction.Marker, "Kadr.Marker", new ShortcutGesture(0x36, ShortcutModifiers.None), DrawingToolId.Marker),
        new(EditorAction.Text, "Kadr.Text", new ShortcutGesture(0x54, ShortcutModifiers.None), DrawingToolId.Text),
        new(EditorAction.Number, "Kadr.Numbering", new ShortcutGesture(0x4e, ShortcutModifiers.None), DrawingToolId.Number),
        new(EditorAction.Blur, "Kadr.Blur", new ShortcutGesture(0x42, ShortcutModifiers.None), DrawingToolId.Blur),
        new(EditorAction.Cursor, "Kadr.ToggleCursor", new ShortcutGesture(0x43, ShortcutModifiers.None)),
        new(EditorAction.Preview, "Kadr.ScreenshotPreview", new ShortcutGesture(0x12, ShortcutModifiers.None)),
        new(EditorAction.Copy, "Kadr.Copy", new ShortcutGesture(0x43, ShortcutModifiers.Control)),
        new(EditorAction.Save, "Kadr.SaveAs", new ShortcutGesture(0x53, ShortcutModifiers.Control), DefaultAlternative: new ShortcutGesture(0x53, ShortcutModifiers.Control | ShortcutModifiers.Shift)),
        new(EditorAction.Recognize, "Kadr.Recognize", new ShortcutGesture(0x54, ShortcutModifiers.Control)),
        new(EditorAction.Undo, "Kadr.Undo", new ShortcutGesture(0x5a, ShortcutModifiers.Control)),
        new(EditorAction.Redo, "Kadr.Redo", new ShortcutGesture(0x59, ShortcutModifiers.Control), DefaultAlternative: new ShortcutGesture(0x5a, ShortcutModifiers.Control | ShortcutModifiers.Shift)),
        new(EditorAction.Cancel, "Kadr.DeselectOrExit", new ShortcutGesture(0x1b, ShortcutModifiers.None)),
        new(EditorAction.Select, "Kadr.SelectObjects", new ShortcutGesture(0xc0, ShortcutModifiers.None), DefaultAlternative: new ShortcutGesture(0x04, ShortcutModifiers.None)),
        new(EditorAction.Delete, "Kadr.DeleteElement", new ShortcutGesture(0x02, ShortcutModifiers.None), DefaultAlternative: ShortcutPolicy.DeleteGesture),
        new(EditorAction.SelectAll, "Kadr.SelectAll", new ShortcutGesture(0x41, ShortcutModifiers.Control))
    };

    public static ShortcutGesture GetDefault(EditorAction action)
    {
        return Definitions.Single(definition => definition.Action == action).DefaultGesture;
    }

    public static ShortcutGesture Get(AppSettings settings, EditorAction action)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return action switch
        {
            EditorAction.Capture => settings.CaptureShortcut,
            EditorAction.Cursor => settings.CursorShortcut,
            EditorAction.Undo => settings.UndoShortcut,
            EditorAction.Redo => settings.RedoShortcut,
            _ => settings.EditorShortcuts.GetValueOrDefault(action) ?? GetDefault(action)
        };
    }

    public static ShortcutGesture? GetAlternative(AppSettings settings, EditorAction action)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.AlternativeShortcuts.TryGetValue(action, out ShortcutGesture? gesture)
            ? gesture : Definitions.Single(definition => definition.Action == action).DefaultAlternative;
    }

    public static IReadOnlyList<ShortcutGesture> GetBindings(AppSettings settings, EditorAction action)
    {
        ShortcutGesture primary = Get(settings, action);
        IEnumerable<ShortcutGesture> candidates = GetAlternative(settings, action) is ShortcutGesture alternative
            ? new ShortcutGesture[] { primary, alternative } : new ShortcutGesture[] { primary };
        return candidates.Where(gesture => !gesture.IsEmpty).ToArray();
    }

    public static AppSettings Apply(AppSettings settings, IReadOnlyDictionary<EditorAction, ShortcutGesture> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(shortcuts);
        return settings with
        {
            CaptureShortcut = shortcuts[EditorAction.Capture],
            CursorShortcut = shortcuts[EditorAction.Cursor],
            UndoShortcut = shortcuts[EditorAction.Undo],
            RedoShortcut = shortcuts[EditorAction.Redo],
            EditorShortcuts = shortcuts.Where(pair => pair.Key is not (EditorAction.Capture or EditorAction.Cursor or EditorAction.Undo or EditorAction.Redo))
                .ToDictionary(pair => pair.Key, pair => pair.Value)
        };
    }
}
