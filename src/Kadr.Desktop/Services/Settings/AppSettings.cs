using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Export;

namespace Kadr.Desktop.Services.Settings;

public sealed record AppSettings
{
    public string? LocalizationId { get; init; }
    public CaptureBackend Backend
    {
        get; init;
    } = CaptureBackend.DesktopDuplication;
    public CaptureMode Mode
    {
        get; init;
    } = CaptureMode.OnDemand;
    public bool ShowCursor { get; init; }
    public uint Color { get; init; } = DrawingDefaults.Color;
    public double Thickness { get; init; } = DrawingDefaults.Thickness;
    public double BlurStrength { get; init; } = DrawingDefaults.BlurStrength;
    public bool AutoSelectCreatedElements { get; init; } = true;
    public OutsideSelectionBehavior OutsideSelectionBehavior { get; init; } = OutsideSelectionBehavior.Reselect;
    public bool ExitImmediately { get; init; } = true;
    public bool HideCursorWhileDrawing { get; init; }
    public ColorPickerTab ColorPickerTab { get; init; }
    public ToolbarItemId[] HiddenToolbarItems { get; init; } = Array.Empty<ToolbarItemId>();
    public ShortcutGesture CaptureShortcut { get; init; } = ShortcutCatalog.GetDefault(EditorAction.Capture);
    public ShortcutGesture CursorShortcut { get; init; } = ShortcutCatalog.GetDefault(EditorAction.Cursor);
    public ShortcutGesture UndoShortcut { get; init; } = ShortcutCatalog.GetDefault(EditorAction.Undo);
    public ShortcutGesture RedoShortcut { get; init; } = ShortcutCatalog.GetDefault(EditorAction.Redo);
    public Dictionary<EditorAction, ShortcutGesture> EditorShortcuts { get; init; } = [];
    public Dictionary<EditorAction, ShortcutGesture?> AlternativeShortcuts { get; init; } = [];
    public ScreenshotFormat CopyFormat { get; init; } = ScreenshotFormat.Png;
    public string[] OcrLanguages { get; init; } = new string[] { "rus", "eng" };
    public string? SaveDirectory
    {
        get; init;
    }
    public string SaveFormat { get; init; } = "png";
    public bool AutoStart
    {
        get; init;
    } = true;
}
