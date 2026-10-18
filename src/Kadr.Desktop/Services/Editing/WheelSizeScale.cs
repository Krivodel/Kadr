namespace Kadr.Desktop.Services.Editing;

public sealed class WheelSizeScale
{
    private static readonly TimeSpan AccelerationTimeout = TimeSpan.FromMilliseconds(140);
    private TimeSpan? _lastInput;
    private double _travel;
    private int _direction;

    public static double GetTarget(double current, double delta, double minimum, double maximum)
    {
        if (!double.IsFinite(current) || !double.IsFinite(delta))
        {
            throw new ArgumentOutOfRangeException(nameof(delta), "Wheel values must be finite.");
        }
        return Math.Clamp(current + delta, minimum, maximum);
    }

    public double GetAcceleratedTarget(double current, double delta, double minimum, double maximum, TimeSpan timestamp)
    {
        if (!double.IsFinite(delta))
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }
        if (delta == 0)
        {
            return GetTarget(current, delta, minimum, maximum);
        }
        int direction = Math.Sign(delta);
        if (_lastInput is not TimeSpan previous || timestamp < previous || timestamp - previous > AccelerationTimeout || direction != _direction)
        {
            _travel = 0;
        }
        double distance = Math.Abs(delta);
        double start = _travel;
        _travel += distance;
        _lastInput = timestamp;
        _direction = direction;
        double movement = IntegrateGain(_travel) - IntegrateGain(start);
        return GetTarget(current, direction * movement, minimum, maximum);
    }

    public void Reset()
    {
        _lastInput = null;
        _travel = 0;
        _direction = 0;
    }

    private static double IntegrateGain(double travel)
    {
        const double Acceleration = 3;
        const double MaximumGain = 24;
        double accelerated = Math.Min(Math.Max(0, travel - 1), (MaximumGain - 1) / Acceleration);
        double capped = Math.Max(0, travel - 1 - accelerated);
        return Math.Min(travel, 1) + accelerated + Acceleration * accelerated * accelerated / 2 + capped * MaximumGain;
    }
}
