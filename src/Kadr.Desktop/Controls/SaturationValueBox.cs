using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using Kadr.Desktop.Services.Geometry;

namespace Kadr.Desktop.Controls;

public sealed class SaturationValueBox : Control
{
    public double Hue
    {
        get; set;
    }
    public double Saturation { get; private set; } = 1;
    public double Value { get; private set; } = 1;

    public event EventHandler? ColorChanged;

    private bool _dragging;
    private bool _hueDragging;

    public void SetHsv(double hue, double saturation, double value)
    {
        Hue = hue;
        Saturation = saturation;
        Value = value;
        InvalidateVisual();
    }

    public void SetColor(uint color)
    {
        (double hue, double saturation, double value) = ColorMath.ToHsv(color);
        if (saturation > 0)
        {
            Hue = hue;
        }
        Saturation = saturation;
        Value = value;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        Point center = new(Bounds.Width / 2, Bounds.Height / 2);
        Rect field = GetFieldBounds();
        double diameter = Math.Min(Bounds.Width, Bounds.Height);
        double thickness = diameter * 0.085;
        double radius = (diameter - thickness) / 2 - 7;
        ConicGradientBrush hue = new() { Angle = 90, GradientStops = ColorBrushes.CreateHueStops() };
        context.DrawEllipse(null, new Pen(hue, thickness), center, radius, radius);
        double angle = Hue * Math.PI / 180;
        Point marker = new(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
        DrawMarker(context, marker, Color.FromUInt32(ColorMath.FromHsv(Hue, 1, 1, 255)));
        LinearGradientBrush horizontal = new()
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = new GradientStops { new(Colors.White, 0), new(Color.FromUInt32(ColorMath.FromHsv(Hue, 1, 1, 255)), 1) }
        };
        LinearGradientBrush vertical = new()
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops { new(Colors.Transparent, 0), new(Colors.Black, 1) }
        };
        context.DrawRectangle(horizontal, null, field, 0, 0);
        context.DrawRectangle(vertical, null, field, 0, 0);
        Point point = new(field.X + Saturation * field.Width, field.Y + (1 - Value) * field.Height);
        DrawMarker(context, point, Color.FromUInt32(ColorMath.FromHsv(Hue, Saturation, Value, 255)));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs arguments)
    {
        if (!arguments.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        Point position = arguments.GetPosition(this);
        Point center = new(Bounds.Width / 2, Bounds.Height / 2);
        double distance = Math.Sqrt(Math.Pow(position.X - center.X, 2) + Math.Pow(position.Y - center.Y, 2));
        _hueDragging = distance > Math.Min(Bounds.Width, Bounds.Height) * 0.385;
        if (!_hueDragging && !GetFieldBounds().Contains(position))
        {
            return;
        }
        _dragging = true;
        arguments.Pointer.Capture(this);
        Update(arguments.GetPosition(this));
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
        if (_hueDragging)
        {
            Hue = (Math.Atan2(point.Y - Bounds.Height / 2, point.X - Bounds.Width / 2) * 180 / Math.PI + 360) % 360;
        }
        else
        {
            Rect field = GetFieldBounds();
            Saturation = Math.Clamp((point.X - field.X) / Math.Max(1, field.Width), 0, 1);
            Value = 1 - Math.Clamp((point.Y - field.Y) / Math.Max(1, field.Height), 0, 1);
        }
        InvalidateVisual();
        ColorChanged?.Invoke(this, EventArgs.Empty);
    }

    private Rect GetFieldBounds()
    {
        double size = (Math.Min(Bounds.Width, Bounds.Height) * 0.415 - 15) * Math.Sqrt(2);
        return new Rect((Bounds.Width - size) / 2, (Bounds.Height - size) / 2, size, size);
    }

    private static void DrawMarker(DrawingContext context, Point point, Color color)
    {
        context.DrawEllipse(new SolidColorBrush(color), new Pen(new SolidColorBrush(Color.Parse("#80000000")), 6), point, 10, 10);
        context.DrawEllipse(null, new Pen(Brushes.White, 3), point, 10, 10);
    }
}
