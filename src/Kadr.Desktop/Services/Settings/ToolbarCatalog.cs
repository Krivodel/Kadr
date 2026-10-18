using Kadr.Desktop.Services.Editing;

namespace Kadr.Desktop.Services.Settings;

public static class ToolbarCatalog
{
    public static IReadOnlyList<ToolbarItemDefinition> Definitions { get; } = CreateDefinitions();

    private static readonly IReadOnlyDictionary<DrawingToolId, ToolbarItemId> ToolItems = Definitions
        .Where(definition => definition.Tool.HasValue).ToDictionary(definition => definition.Tool.GetValueOrDefault(), definition => definition.Id);

    public static ToolbarItemId ForTool(DrawingToolId tool)
    {
        return ToolItems[tool];
    }

    public static bool CanHide(ToolbarItemId item)
    {
        return item != ToolbarItemId.Settings;
    }

    public static bool IsActionPanel(ToolbarItemId item)
    {
        return item is ToolbarItemId.Copy or ToolbarItemId.Save or ToolbarItemId.Recognize or ToolbarItemId.Settings or ToolbarItemId.Cancel;
    }

    private static IReadOnlyList<ToolbarItemDefinition> CreateDefinitions()
    {
        Dictionary<ToolbarItemId, string> captions = new Dictionary<ToolbarItemId, string>
        {
            [ToolbarItemId.Settings] = "Kadr.Settings",
            [ToolbarItemId.Cancel] = "Kadr.Cancel",
            [ToolbarItemId.Color] = "Kadr.Color",
            [ToolbarItemId.Thickness] = "Kadr.Thickness",
            [ToolbarItemId.Cursor] = "Kadr.Cursor"
        };
        Dictionary<string, ShortcutDefinition> shortcuts = ShortcutCatalog.Definitions.ToDictionary(definition => definition.Action.ToString(), StringComparer.Ordinal);
        return Enum.GetValues<ToolbarItemId>().Select(id =>
        {
            shortcuts.TryGetValue(id.ToString(), out ShortcutDefinition? shortcut);
            string caption = captions.GetValueOrDefault(id) ?? shortcut?.NameKey
                ?? throw new InvalidOperationException($"Toolbar item {id} has no caption.");
            return new ToolbarItemDefinition(id, caption, shortcut?.Tool);
        }).ToArray();
    }
}
