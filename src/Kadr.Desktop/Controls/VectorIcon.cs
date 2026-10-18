using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Kadr.Desktop.Controls;

public sealed class VectorIcon : Control
{

    public bool IsFilled
    {
        get => GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    public bool IsBlurred
    {
        get => GetValue(IsBlurredProperty);
        set => SetValue(IsBlurredProperty, value);
    }

    public string Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }
    public static readonly StyledProperty<bool> IsFilledProperty = AvaloniaProperty.Register<VectorIcon, bool>(nameof(IsFilled));
    public static readonly StyledProperty<string> DataProperty = AvaloniaProperty.Register<VectorIcon, string>(nameof(Data), string.Empty);
    public static readonly StyledProperty<bool> IsBlurredProperty = AvaloniaProperty.Register<VectorIcon, bool>(nameof(IsBlurred));

    private string? _parsedData;
    private Geometry? _geometry;

    static VectorIcon()
    {
        AffectsRender<VectorIcon>(DataProperty, IsBlurredProperty, IsFilledProperty);
    }

    public override void Render(DrawingContext context)
    {
        if (IsBlurred)
        {
            using (context.PushEffect(new BlurEffect { Radius = 5.5 }, new Rect(Bounds.Size)))
            {
                context.FillRectangle(Brushes.White, new Rect(1, 1, Math.Max(0, Bounds.Width - 2), Math.Max(0, Bounds.Height - 2)));
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(Data))
        {
            return;
        }

        if (_parsedData != Data)
        {
            _geometry = Geometry.Parse(Data);
            _parsedData = Data;
        }

        using (context.PushTransform(Matrix.CreateScale(Bounds.Width / 24, Bounds.Height / 24)))
        {
            if (_geometry is Geometry geometry)
            {
                context.DrawGeometry(IsFilled ? Brushes.White : null,
                    IsFilled ? null : new Pen(Brushes.White, 1.65, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
            }
        }
    }
}
