using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.Services.Rendering;

namespace Kadr.Desktop.ViewModels;

public sealed class ToolbarVisibilityViewModel
{
    public bool Copy => Contains(ToolbarItemId.Copy);
    public bool Save => Contains(ToolbarItemId.Save);
    public bool Recognize => Contains(ToolbarItemId.Recognize);
    public bool Settings => Contains(ToolbarItemId.Settings);
    public bool Cancel => Contains(ToolbarItemId.Cancel);
    public bool Redo => Contains(ToolbarItemId.Redo);
    public bool Undo => Contains(ToolbarItemId.Undo);
    public bool Color => Contains(ToolbarItemId.Color);
    public bool Thickness => Contains(ToolbarItemId.Thickness);
    public bool Cursor => Contains(ToolbarItemId.Cursor);
    public bool HasActions => HasExport || Settings || Cancel;
    public bool HasExport => Copy || Save || Recognize;
    public bool HasHistory => Redo || Undo;
    public bool HasStyle => Color || Thickness || Cursor;
    public IReadOnlyList<DrawingTool> LabelTools { get; }
    public IReadOnlyList<DrawingTool> DrawingTools { get; }
    public bool HasLabels => LabelTools.Count > 0;
    public bool HasDrawing => DrawingTools.Count > 0;
    public bool HasTools => HasHistory || HasStyle || HasLabels || HasDrawing;
    public bool ActionsDivider => HasExport && (Settings || Cancel);
    public bool StyleDivider => HasHistory && HasStyle;
    public bool LabelsDivider => (HasHistory || HasStyle) && HasLabels;
    public bool DrawingDivider => (HasHistory || HasStyle || HasLabels) && HasDrawing;

    private readonly HashSet<ToolbarItemId> _hidden;

    public ToolbarVisibilityViewModel(IEnumerable<ToolbarItemId> hidden, IReadOnlyList<DrawingTool> tools)
    {
        ArgumentNullException.ThrowIfNull(hidden);
        ArgumentNullException.ThrowIfNull(tools);
        _hidden = hidden.Where(ToolbarCatalog.CanHide).ToHashSet();
        LabelTools = tools.TakeWhile(tool => !tool.StartsGroup).Where(tool => ContainsTool(tool.Id)).ToArray();
        DrawingTools = tools.SkipWhile(tool => !tool.StartsGroup).Where(tool => ContainsTool(tool.Id)).ToArray();
    }

    public bool Contains(ToolbarItemId item)
    {
        return !_hidden.Contains(item);
    }

    public bool ContainsTool(DrawingToolId tool)
    {
        return Contains(ToolbarCatalog.ForTool(tool));
    }
}
