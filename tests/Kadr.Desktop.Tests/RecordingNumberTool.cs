using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Rendering;
using SkiaSharp;

namespace Kadr.Desktop.Tests;

internal sealed class RecordingNumberTool : DrawingTool
{
    public override DrawingToolId Id => DrawingToolId.Number;
    public override string Name => "Recording number";
    public override string Icon => "";
    internal List<int> Numbers { get; } = [];

    public override void Draw(SKCanvas canvas, DrawingElement element, int number, DrawingSource source)
    {
        Numbers.Add(number);
    }
}
