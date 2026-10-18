using SkiaSharp;

using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Editing;

namespace Kadr.Desktop.Services.Rendering;

public sealed class BlurTool : DrawingTool
{
    public override bool HasShadow => false;
    public override DrawingToolId Id => DrawingToolId.Blur;
    public override string Name => UiText.Get("Kadr.Blur");
    public override bool HasBlurredIcon => true;
    public override bool UsesSource => true;
    public override string Icon => "M3 3H21V21H3Z";

    public override void Draw(SKCanvas canvas, DrawingElement element, int number, DrawingSource source)
    {
        float sigma = (float)Math.Clamp(element.BlurStrength, DrawingDefaults.MinimumBlurStrength, DrawingDefaults.MaximumBlurStrength);
        canvas.Save();
        canvas.ClipRect(GetRectangle(element));
        canvas.DrawImage(source.GetBlurredImage(sigma), source.Bounds,
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        canvas.Restore();
    }
}
