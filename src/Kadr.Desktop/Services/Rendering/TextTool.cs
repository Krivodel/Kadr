using SkiaSharp;

using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Editing;

namespace Kadr.Desktop.Services.Rendering;

public sealed class TextTool : DrawingTool
{
    public override DrawingToolId Id => DrawingToolId.Text;
    public override string Name => UiText.Get("Kadr.Text");
    public override string Icon => "M4 5H20 M12 5V20 M8 20H16";

    public override void Draw(SKCanvas canvas, DrawingElement element, int number, DrawingSource source)
    {
        using SKPaint paint = CreatePaint(element);
        paint.Style = SKPaintStyle.Fill;
        using SKTypeface typeface = SKTypeface.FromFamilyName(DrawingDefaults.TypefaceName);
        using SKFont font = new(typeface, (float)DrawingDefaults.GetLabelSize(element.Thickness));
        float top = (float)element.Bounds.Y - font.Metrics.Ascent;

        foreach (string line in element.Text.Split('\n'))
        {
            canvas.DrawText(line, (float)element.Bounds.X, top, font, paint);
            top += font.Size * (float)DrawingDefaults.TextLineSpacing;
        }
    }
}
