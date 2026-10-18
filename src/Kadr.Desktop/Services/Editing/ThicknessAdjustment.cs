namespace Kadr.Desktop.Services.Editing;

public sealed class ThicknessAdjustment
{
    public double Value
    {
        get; private set;
    }

    private const double PixelsPerUnit = 8;
    private double _previousX;

    public ThicknessAdjustment(double value, double pointerX)
    {
        Value = Math.Clamp(value, DrawingDefaults.MinimumThickness, DrawingDefaults.MaximumThickness);
        _previousX = pointerX;
    }

    public void Rebase(double pointerX)
    {
        _previousX = pointerX;
    }

    public void Update(double pointerX, bool precise)
    {
        double delta = (pointerX - _previousX) / (PixelsPerUnit * (precise ? 3 : 1));
        _previousX = pointerX;
        Value = Math.Clamp(Value + delta, DrawingDefaults.MinimumThickness, DrawingDefaults.MaximumThickness);
    }
}
