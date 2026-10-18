using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Kadr.Desktop.Controls;

public sealed class ColorChip : Control
{
    public uint ColorValue
    {
        get => GetValue(ColorValueProperty);
        set => SetValue(ColorValueProperty, value);
    }
    public static readonly StyledProperty<uint> ColorValueProperty = AvaloniaProperty.Register<ColorChip, uint>(nameof(ColorValue), 0xffffffff);

    static ColorChip()
    {
        AffectsRender<ColorChip>(ColorValueProperty);
    }

    public override void Render(DrawingContext context)
    {
        Rect area = new(Bounds.Size);
        using (context.PushClip(new RoundedRect(area, 7)))
        {
            ColorBrushes.DrawTransparencyGrid(context, area);
            context.FillRectangle(new SolidColorBrush(Color.FromUInt32(ColorValue)), area);
        }
    }
}
