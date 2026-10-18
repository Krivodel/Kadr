namespace Kadr.Desktop.Services.Editing;

public sealed class WheelSizeTransition
{
    public static TimeSpan Duration => TimeSpan.FromMilliseconds(190);
    public static TimeSpan BurstTimeout => TimeSpan.FromMilliseconds(250);
    public double Value { get; private set; }
    public double Target { get; private set; }
    public bool IsActive { get; private set; }

    private TimeSpan _lastUpdate;
    private TimeSpan _lastInput;

    public void Begin(double current, double target, TimeSpan timestamp)
    {
        if (!double.IsFinite(current) || !double.IsFinite(target))
        {
            throw new ArgumentOutOfRangeException(nameof(target), "Wheel values must be finite.");
        }
        Value = IsActive ? Advance(timestamp) : current;
        Target = target;
        _lastUpdate = _lastInput = timestamp;
        IsActive = Value != Target;
    }

    public double Advance(TimeSpan timestamp)
    {
        const double ResponseMilliseconds = 35;
        double elapsed = Math.Max(0, (timestamp - _lastUpdate).TotalMilliseconds);
        Value = Target + (Value - Target) * Math.Exp(-elapsed / ResponseMilliseconds);
        _lastUpdate = timestamp;
        if (timestamp - _lastInput >= Duration)
        {
            Value = Target;
        }
        IsActive = Value != Target;
        return Value;
    }

    public double Finish()
    {
        IsActive = false;
        Value = Target;
        return Value;
    }
}
