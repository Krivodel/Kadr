using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;

namespace Kadr.Desktop.Controls;

public sealed class PalettePanel : Border
{
    protected override Type StyleKeyOverride => typeof(Border);
    public static readonly StyledProperty<double> RevealProperty = AvaloniaProperty.Register<PalettePanel, double>(nameof(Reveal));
    public double Reveal
    {
        get => GetValue(RevealProperty);
        set => SetValue(RevealProperty, value);
    }

    private const double FoldAngle = 36;
    private const double FoldDepth = 500;
    private const double Travel = 12;
    private const double ItemTravel = 8;
    private const double StaggerFraction = 0.54;
    private const double MinimumItemScale = 0.88;
    private readonly Rotate3DTransform _fold = new() { Depth = FoldDepth };
    private readonly TranslateTransform _travel = new();
    private Control[] _items = Array.Empty<Control>();
    private bool _visible;

    static PalettePanel()
    {
        RevealProperty.Changed.AddClassHandler<PalettePanel>((panel, arguments) => panel.UpdateAppearance());
    }

    public PalettePanel()
    {
        Transitions = new Transitions
        {
            new DoubleTransition { Property = RevealProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new LinearEasing() }
        };
        RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
        RenderTransform = new TransformGroup { Children = new Transforms { _fold, _travel } };
        IsHitTestVisible = false;
        UpdateAppearance();
    }

    public void InitializeItems(IEnumerable<Control> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items.ToArray();
        foreach (Control item in _items)
        {
            item.RenderTransformOrigin = RelativePoint.Center;
            item.RenderTransform = new TransformGroup { Children = new Transforms { new ScaleTransform(), new TranslateTransform() } };
        }
        UpdateAppearance();
    }

    public void SetVisible(bool visible)
    {
        if (_visible == visible)
        {
            return;
        }
        _visible = visible;
        IsHitTestVisible = visible;
        Reveal = visible ? 1 : 0;
    }

    private void UpdateAppearance()
    {
        double progress = Math.Clamp(Reveal, 0, 1);
        double folded = Math.Pow(1 - progress, 3);
        Opacity = Math.Min(1, progress * 4);
        _fold.AngleX = -FoldAngle * folded;
        _fold.AngleY = 5 * folded;
        _travel.Y = -Travel * folded;
        for (int index = 0; index < _items.Length; index++)
        {
            Control item = _items[index];
            int row = index / 3;
            int lastRow = Math.Max(1, (_items.Length - 1) / 3);
            double offset = StaggerFraction * row / lastRow;
            double phase = Math.Clamp((progress - offset) / (1 - StaggerFraction), 0, 1);
            double amount = 1 - Math.Pow(1 - phase, 3);
            item.Opacity = amount;
            if (item.RenderTransform is TransformGroup group && group.Children[0] is ScaleTransform scale && group.Children[1] is TranslateTransform translation)
            {
                scale.ScaleX = scale.ScaleY = MinimumItemScale + (1 - MinimumItemScale) * amount;
                translation.Y = -ItemTravel * (1 - amount);
            }
        }
    }
}
