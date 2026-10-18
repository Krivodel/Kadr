using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;

namespace Kadr.Desktop.Controls;

public sealed class AnimatedPopupPanel : Border
{
    public static readonly StyledProperty<double> RevealProperty = AvaloniaProperty.Register<AnimatedPopupPanel, double>(nameof(Reveal));
    public double Reveal
    {
        get => GetValue(RevealProperty);
        set => SetValue(RevealProperty, value);
    }
    public event EventHandler? Hidden;

    private readonly TranslateTransform _translation = new();
    private bool _shown;

    static AnimatedPopupPanel()
    {
        RevealProperty.Changed.AddClassHandler<AnimatedPopupPanel>((panel, arguments) => panel.UpdateAppearance());
    }

    public AnimatedPopupPanel()
    {
        Transitions = new Transitions
        {
            new DoubleTransition { Property = RevealProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = new CubicEaseOut() }
        };
        RenderTransform = _translation;
        UpdateAppearance();
    }

    public void SetVisible(bool visible)
    {
        _shown = visible;
        Reveal = visible ? 1 : 0;
        UpdateAppearance();
    }

    private void UpdateAppearance()
    {
        double progress = Math.Clamp(Reveal, 0, 1);
        Opacity = progress;
        _translation.Y = -6 * (1 - progress);
        if (!_shown && progress == 0)
        {
            Hidden?.Invoke(this, EventArgs.Empty);
        }
    }
}
