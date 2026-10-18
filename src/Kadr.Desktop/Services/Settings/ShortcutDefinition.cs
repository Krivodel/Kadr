using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Editing;

namespace Kadr.Desktop.Services.Settings;

public sealed record ShortcutDefinition(EditorAction Action, string NameKey, ShortcutGesture DefaultGesture, DrawingToolId? Tool = null, ShortcutGesture? DefaultAlternative = null)
{
    public string Name => UiText.Get(NameKey);
}
