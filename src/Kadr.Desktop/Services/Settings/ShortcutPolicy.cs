using Kadr.Desktop.Resources;

namespace Kadr.Desktop.Services.Settings;

public static class ShortcutPolicy
{
    public static ShortcutGesture DeleteGesture { get; } = new(0x2e, ShortcutModifiers.None);

    public static string? GetError(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? structureError = GetStructureError(settings);
        if (structureError is not null)
        {
            return structureError;
        }
        ShortcutGesture[] shortcuts = GetGestures(settings);

        if (shortcuts.Distinct().Count() != shortcuts.Length)
        {
            return UiText.Get("Kadr.DuplicateShortcut");
        }

        foreach (ShortcutDefinition definition in ShortcutCatalog.Definitions)
        {
            foreach (ShortcutGesture gesture in ShortcutCatalog.GetBindings(settings, definition.Action))
            {
                if (((gesture == DeleteGesture) && definition.Action != EditorAction.Delete)
                    || ((gesture.Key == 0x1b) && (definition.Action != EditorAction.Cancel)))
                {
                    return UiText.Get("Kadr.ReservedShortcut");
                }
            }
        }

        return null;
    }
    public static string? GetStructureError(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.AlternativeShortcuts is null || settings.AlternativeShortcuts.Keys.Any(key => !Enum.IsDefined(key)) || settings.EditorShortcuts is null || settings.EditorShortcuts.Values.Any(gesture => gesture is null))
        {
            return UiText.Get("Kadr.MissingShortcuts");
        }
        IEnumerable<ShortcutGesture> configured = ShortcutCatalog.Definitions.Select(definition => ShortcutCatalog.Get(settings, definition.Action))
            .Concat(settings.AlternativeShortcuts.Values.OfType<ShortcutGesture>());
        if (configured.Any(shortcut => (shortcut is null) || ((shortcut.Key < 8) && shortcut.Key is not (0 or 2 or 4))
            || (shortcut.Key == 0 && shortcut.Modifiers != ShortcutModifiers.None) || shortcut.Key < 0 || (shortcut.Key > 254)
            || (((uint)shortcut.Modifiers & ~15u) != 0)))
        {
            return UiText.Get("Kadr.InvalidShortcut");
        }
        return null;
    }

    private static ShortcutGesture[] GetGestures(AppSettings settings)
    {
        return ShortcutCatalog.Definitions.SelectMany(definition => ShortcutCatalog.GetBindings(settings, definition.Action)).ToArray();
    }

}
