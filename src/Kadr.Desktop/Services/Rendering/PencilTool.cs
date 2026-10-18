using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Editing;

namespace Kadr.Desktop.Services.Rendering;

public sealed class PencilTool : StrokeTool
{
    public override DrawingToolId Id => DrawingToolId.Pencil;
    public override string Name => UiText.Get("Kadr.Pencil");
    public override string Icon => "M4 20L8 19L20 7L17 4L5 16Z M14 7L17 10";
    protected override byte Opacity => 255;
}
