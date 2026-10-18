using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Kadr.Desktop.Controls.Scrolling;

public sealed class SoftScrollViewer : ScrollViewer
{
    public static Thickness ContentInsets => new(0, FadeLength);

    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    private const double FadeLength = 16;
    private readonly GradientStop _top = new(Colors.Transparent, 0);
    private readonly GradientStop _topEnd = new(Colors.Black, 0);
    private readonly GradientStop _bottomStart = new(Colors.Black, 1);
    private readonly GradientStop _bottom = new(Colors.Transparent, 1);
    private ScrollContentPresenter? _presenter;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs arguments)
    {
        base.OnApplyTemplate(arguments);
        _presenter = arguments.NameScope.Find<ScrollContentPresenter>("PART_ContentPresenter");
        if (_presenter is not null)
        {
            _presenter.OpacityMask = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops { _top, _topEnd, _bottomStart, _bottom }
            };
        }
        UpdateFade();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs arguments)
    {
        base.OnPropertyChanged(arguments);
        if (arguments.Property == ViewportProperty)
        {
            UpdateFade();
        }
    }

    private void UpdateFade()
    {
        double fade = Math.Min(0.5, FadeLength / Math.Max(1, Viewport.Height));
        _topEnd.Offset = fade;
        _bottomStart.Offset = 1 - fade;
    }
}
