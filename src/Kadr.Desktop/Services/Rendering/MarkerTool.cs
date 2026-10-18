using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Editing;

namespace Kadr.Desktop.Services.Rendering;

public sealed class MarkerTool : StrokeTool
{
    public override DrawingToolId Id => DrawingToolId.Marker;
    public override string Name => UiText.Get("Kadr.Marker");
    public override bool StartsGroup => true;
    public override string Icon => "M3 18L5 15L15 5L17 3L21 7L19 9L9 19L6 21Z M5 15L9 19 M15 5L19 9 M2 22H9";
    public override bool HasShadow => false;
    protected override byte Opacity => DrawingDefaults.MarkerOpacity;
}
