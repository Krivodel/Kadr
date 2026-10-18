using Kadr.Desktop.Services.Editing;

namespace Kadr.Desktop.Services.Settings;

public sealed record ToolbarItemDefinition(ToolbarItemId Id, string NameKey, DrawingToolId? Tool = null);
