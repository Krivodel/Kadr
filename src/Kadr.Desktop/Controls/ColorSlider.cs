using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace Kadr.Desktop.Controls;

public sealed class ColorSlider : RangeBase
{
    public bool IsVertical { get; set; }
    public IBrush? TrackBrush { get; set; }
    public bool ShowsTransparency { get; set; }
    public Color MarkerColor { get; set; } = Colors.White;

    private const double TrackHeight = 18;
    private const double MarkerRadius = 11;
    private const double MarkerInset = MarkerRadius + 3;
    private bool _dragging;

    static ColorSlider()
    {
        AffectsRender<ColorSlider>(ValueProperty);
    }

    public ColorSlider()
    {
        MinHeight = 36;
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        Rect track = IsVertical ? new Rect(4, 6, Math.Max(0, Bounds.Width - 8), Math.Max(0, Bounds.Height - 12))
            : new Rect(MarkerInset, (Bounds.Height - TrackHeight) / 2, Math.Max(0, Bounds.Width - MarkerInset * 2), TrackHeight);
        using (context.PushClip(new RoundedRect(track, TrackHeight / 2)))
        {
            if (ShowsTransparency)
            {
                ColorBrushes.DrawTransparencyGrid(context, track);
            }
            context.DrawRectangle(TrackBrush, null, track, TrackHeight / 2, TrackHeight / 2);
        }
        double value = (Value - Minimum) / Math.Max(1, Maximum - Minimum);
        Point marker = IsVertical ? new Point(Bounds.Width / 2, track.Y + (1 - value) * track.Height)
            : new Point(track.X + value * track.Width, Bounds.Height / 2);
        context.DrawEllipse(new SolidColorBrush(MarkerColor), new Pen(new SolidColorBrush(Color.Parse("#50202A40")), 6), marker, MarkerRadius, MarkerRadius);
        context.DrawEllipse(null, new Pen(Brushes.White, 3), marker, MarkerRadius, MarkerRadius);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs arguments)
    {
        if (arguments.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragging = true;
            arguments.Pointer.Capture(this);
            Update(arguments.GetPosition(this));
            arguments.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs arguments)
    {
        if (_dragging)
        {
            Update(arguments.GetPosition(this));
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs arguments)
    {
        _dragging = false;
        arguments.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs arguments)
    {
        _dragging = false;
    }

    private void Update(Point point)
    {
        double normalized = IsVertical ? 1 - (point.Y - 6) / Math.Max(1, Bounds.Height - 12)
            : (point.X - MarkerInset) / Math.Max(1, Bounds.Width - MarkerInset * 2);
        SetCurrentValue(ValueProperty, Minimum + Math.Clamp(normalized, 0, 1) * (Maximum - Minimum));
    }
}
