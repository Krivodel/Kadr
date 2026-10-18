using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia;

namespace Kadr.Desktop.Controls.Scrolling;

internal static class SmoothScrollTargetCalculator
{
    private const double MovementEpsilon = 0.01d;

    internal static bool TryCalculateTargetOffset(
        ScrollViewer scrollViewer,
        SmoothScrollState state,
        Vector wheelDelta,
        double multiplier,
        out Vector targetOffset)
    {
        ArgumentNullException.ThrowIfNull(scrollViewer);
        ArgumentNullException.ThrowIfNull(state);

        Vector baseOffset = state.GetBaseOffset();

        if (TryCalculateTargetOffset(
            scrollViewer,
            baseOffset,
            wheelDelta,
            multiplier,
            out targetOffset))
        {
            return true;
        }

        return TryCalculateActiveBoundaryTarget(
            scrollViewer,
            state,
            wheelDelta,
            out targetOffset);
    }

    internal static bool TryCalculateTargetOffset(
        ScrollViewer scrollViewer,
        Vector baseOffset,
        Vector wheelDelta,
        double multiplier,
        out Vector targetOffset)
    {
        ArgumentNullException.ThrowIfNull(scrollViewer);

        if (TryCalculateVerticalTarget(
            scrollViewer,
            baseOffset,
            wheelDelta.Y,
            multiplier,
            out targetOffset))
        {
            return true;
        }

        return TryCalculateHorizontalTarget(
            scrollViewer,
            baseOffset,
            wheelDelta,
            multiplier,
            out targetOffset);
    }

    private static bool TryCalculateVerticalTarget(
        ScrollViewer scrollViewer,
        Vector baseOffset,
        double wheelDelta,
        double multiplier,
        out Vector targetOffset)
    {
        targetOffset = default;

        if (!CanApplyVerticalWheelDelta(scrollViewer, wheelDelta))
        {
            return false;
        }

        double y = Clamp(
            baseOffset.Y - (wheelDelta * multiplier),
            GetMaxVerticalOffset(scrollViewer));

        if (IsSameCoordinate(baseOffset.Y, y))
        {
            return false;
        }

        targetOffset = new Vector(baseOffset.X, y);
        return true;
    }

    private static bool TryCalculateHorizontalTarget(
        ScrollViewer scrollViewer,
        Vector baseOffset,
        Vector wheelDelta,
        double multiplier,
        out Vector targetOffset)
    {
        targetOffset = default;
        double delta = GetHorizontalWheelDelta(wheelDelta);

        if (!CanApplyHorizontalWheelDelta(scrollViewer, delta))
        {
            return false;
        }

        double x = Clamp(
            baseOffset.X - (delta * multiplier),
            GetMaxHorizontalOffset(scrollViewer));

        if (IsSameCoordinate(baseOffset.X, x))
        {
            return false;
        }

        targetOffset = new Vector(x, baseOffset.Y);
        return true;
    }

    private static bool TryCalculateActiveBoundaryTarget(
        ScrollViewer scrollViewer,
        SmoothScrollState state,
        Vector wheelDelta,
        out Vector targetOffset)
    {
        targetOffset = default;

        if (!state.IsRunning)
        {
            return false;
        }

        Vector currentOffset = scrollViewer.Offset;
        Vector activeTargetOffset = state.TargetOffset;

        if (CanContinueVerticalBoundaryTarget(
            scrollViewer,
            currentOffset,
            activeTargetOffset,
            wheelDelta.Y))
        {
            targetOffset = activeTargetOffset;
            return true;
        }

        if (CanContinueHorizontalBoundaryTarget(
            scrollViewer,
            currentOffset,
            activeTargetOffset,
            wheelDelta))
        {
            targetOffset = activeTargetOffset;
            return true;
        }

        return false;
    }

    private static bool CanContinueVerticalBoundaryTarget(
        ScrollViewer scrollViewer,
        Vector currentOffset,
        Vector targetOffset,
        double wheelDelta)
    {
        if (!CanApplyVerticalWheelDelta(scrollViewer, wheelDelta))
        {
            return false;
        }

        double maximum = GetMaxVerticalOffset(scrollViewer);

        return IsMovingTowardTargetBoundary(
            currentOffset.Y,
            targetOffset.Y,
            wheelDelta,
            maximum);
    }

    private static bool CanContinueHorizontalBoundaryTarget(
        ScrollViewer scrollViewer,
        Vector currentOffset,
        Vector targetOffset,
        Vector wheelDelta)
    {
        double delta = GetHorizontalWheelDelta(wheelDelta);

        if (!CanApplyHorizontalWheelDelta(scrollViewer, delta))
        {
            return false;
        }

        double maximum = GetMaxHorizontalOffset(scrollViewer);

        return IsMovingTowardTargetBoundary(
            currentOffset.X,
            targetOffset.X,
            delta,
            maximum);
    }

    private static bool IsMovingTowardTargetBoundary(
        double currentValue,
        double targetValue,
        double wheelDelta,
        double maximum)
    {
        if (!IsAwayFromTarget(currentValue, targetValue))
        {
            return false;
        }

        if (wheelDelta > 0d)
        {
            return IsSameCoordinate(targetValue, 0d);
        }

        return IsSameCoordinate(targetValue, maximum);
    }

    private static double GetHorizontalWheelDelta(Vector wheelDelta)
    {
        return wheelDelta.Y != 0d ? wheelDelta.Y : wheelDelta.X;
    }

    private static bool CanApplyVerticalWheelDelta(
        ScrollViewer scrollViewer,
        double wheelDelta)
    {
        return scrollViewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled
            && GetMaxVerticalOffset(scrollViewer) > 0d
            && wheelDelta != 0d;
    }

    private static bool CanApplyHorizontalWheelDelta(
        ScrollViewer scrollViewer,
        double wheelDelta)
    {
        return scrollViewer.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled
            && GetMaxHorizontalOffset(scrollViewer) > 0d
            && wheelDelta != 0d;
    }

    private static double GetMaxVerticalOffset(ScrollViewer scrollViewer)
    {
        return Math.Max(0d, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
    }

    private static double GetMaxHorizontalOffset(ScrollViewer scrollViewer)
    {
        return Math.Max(0d, scrollViewer.Extent.Width - scrollViewer.Viewport.Width);
    }

    private static double Clamp(double value, double maximum)
    {
        return Math.Clamp(value, 0d, maximum);
    }

    private static bool IsSameCoordinate(double currentValue, double targetValue)
    {
        return Math.Abs(currentValue - targetValue) < MovementEpsilon;
    }

    private static bool IsAwayFromTarget(double currentValue, double targetValue)
    {
        return Math.Abs(currentValue - targetValue) > MovementEpsilon;
    }
}
