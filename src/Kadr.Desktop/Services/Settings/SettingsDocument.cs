using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kadr.Desktop.Services.Settings;

internal sealed class SettingsDocument
{
    private static readonly JsonSerializerOptions SerializationOptions = new() { WriteIndented = true };
    private readonly JsonObject _root;

    internal SettingsDocument(string json)
    {
        _root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Settings must be an object.");
    }

    internal AppSettings Read()
    {
        JsonObject compatible = (JsonObject)_root.DeepClone();
        foreach (string name in new string[] { nameof(AppSettings.EditorShortcuts), nameof(AppSettings.AlternativeShortcuts) })
        {
            if (compatible[name] is JsonObject shortcuts)
            {
                foreach (string key in shortcuts.Select(property => property.Key).Where(key => !IsKnownAction(key)).ToArray())
                {
                    shortcuts.Remove(key);
                }
            }
        }

        AppSettings settings = compatible.Deserialize<AppSettings>() ?? throw new JsonException("Settings are missing.");
        if (!compatible.ContainsKey(nameof(AppSettings.EditorShortcuts))
            && settings.CursorShortcut == new ShortcutGesture(0x48, ShortcutModifiers.None))
        {
            settings = settings with { CursorShortcut = ShortcutCatalog.GetDefault(EditorAction.Cursor) };
        }

        if (!compatible.ContainsKey(nameof(AppSettings.AlternativeShortcuts))
            && settings.EditorShortcuts?.GetValueOrDefault(EditorAction.Select) == new ShortcutGesture(0x56, ShortcutModifiers.None))
        {
            settings = settings with
            {
                EditorShortcuts = settings.EditorShortcuts.Where(pair => pair.Key != EditorAction.Select).ToDictionary()
            };
        }

        return settings;
    }

    internal string Serialize(AppSettings settings)
    {
        JsonObject updated = JsonSerializer.SerializeToNode(settings) as JsonObject
            ?? throw new JsonException("Settings could not be serialized.");
        JsonObject merged = (JsonObject)_root.DeepClone();
        foreach (KeyValuePair<string, JsonNode?> property in updated)
        {
            JsonNode? value = property.Value?.DeepClone();
            if (property.Key is nameof(AppSettings.EditorShortcuts) or nameof(AppSettings.AlternativeShortcuts)
                && value is JsonObject shortcuts && _root[property.Key] is JsonObject original)
            {
                foreach (KeyValuePair<string, JsonNode?> shortcut in original.Where(shortcut => !IsKnownAction(shortcut.Key)))
                {
                    shortcuts[shortcut.Key] = shortcut.Value?.DeepClone();
                }
            }

            merged[property.Key] = value;
        }

        return merged.ToJsonString(SerializationOptions);
    }

    private static bool IsKnownAction(string name)
    {
        return Enum.TryParse(name, true, out EditorAction action) && Enum.IsDefined(action);
    }
}
