using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia;

namespace Kadr.Desktop.Controls.Scrolling;

public static class SmoothScrollBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>(
            "IsEnabled",
            typeof(SmoothScrollBehavior));
    public static readonly AttachedProperty<TimeSpan> DurationProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, TimeSpan>(
            "Duration",
            typeof(SmoothScrollBehavior),
            TimeSpan.FromMilliseconds(DefaultDurationMilliseconds));
    public static readonly AttachedProperty<double> WheelMultiplierProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, double>(
            "WheelMultiplier",
            typeof(SmoothScrollBehavior),
            DefaultWheelMultiplier);

    private const double DefaultDurationMilliseconds = 240d;
    private const double DefaultWheelMultiplier = 96d;

    private static readonly AttachedProperty<bool> IsLifecycleAttachedProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>(
            "IsLifecycleAttached",
            typeof(SmoothScrollBehavior));
    private static readonly AttachedProperty<SmoothScrollState?> StateProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, SmoothScrollState?>(
            "State",
            typeof(SmoothScrollBehavior));

    static SmoothScrollBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>(
            OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(ScrollViewer scrollViewer)
    {
        ArgumentNullException.ThrowIfNull(scrollViewer);
        return scrollViewer.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(ScrollViewer scrollViewer, bool value)
    {
        ArgumentNullException.ThrowIfNull(scrollViewer);
        scrollViewer.SetValue(IsEnabledProperty, value);
    }

    public static TimeSpan GetDuration(ScrollViewer scrollViewer)
    {
        ArgumentNullException.ThrowIfNull(scrollViewer);
        return scrollViewer.GetValue(DurationProperty);
    }

    public static void SetDuration(ScrollViewer scrollViewer, TimeSpan value)
    {
        ArgumentNullException.ThrowIfNull(scrollViewer);
        scrollViewer.SetValue(DurationProperty, value);
    }

    public static double GetWheelMultiplier(ScrollViewer scrollViewer)
    {
        ArgumentNullException.ThrowIfNull(scrollViewer);
        return scrollViewer.GetValue(WheelMultiplierProperty);
    }

    public static void SetWheelMultiplier(ScrollViewer scrollViewer, double value)
    {
        ArgumentNullException.ThrowIfNull(scrollViewer);
        scrollViewer.SetValue(WheelMultiplierProperty, value);
    }

    public static void ScrollToOffset(ScrollViewer scrollViewer, Vector targetOffset)
    {
        ArgumentNullException.ThrowIfNull(scrollViewer);

        EnsureLifecycleAttached(scrollViewer);
        EnsureAttached(scrollViewer);
        SmoothScrollState? state = GetState(scrollViewer);

        if (state is null)
        {
            return;
        }

        state.Start(targetOffset, GetDuration(scrollViewer));
    }

    public static void ResetOffset(ScrollViewer scrollViewer)
    {
        ArgumentNullException.ThrowIfNull(scrollViewer);
        GetState(scrollViewer)?.CancelAnimation();
        scrollViewer.Offset = default;
    }

    private static void EnsureAttached(ScrollViewer scrollViewer)
    {
        if (GetState(scrollViewer) is not null)
        {
            return;
        }

        SmoothScrollState state = new(scrollViewer);
        SetState(scrollViewer, state);
        scrollViewer.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnPointerWheelChanged,
            RoutingStrategies.Tunnel);
    }

    private static void DetachState(ScrollViewer scrollViewer)
    {
        SmoothScrollState? state = GetState(scrollViewer);

        if (state is null)
        {
            return;
        }

        scrollViewer.RemoveHandler(
            InputElement.PointerWheelChangedEvent,
            OnPointerWheelChanged);
        state.Stop();
        scrollViewer.ClearValue(StateProperty);
    }

    private static void EnsureLifecycleAttached(ScrollViewer scrollViewer)
    {
        if (GetIsLifecycleAttached(scrollViewer))
        {
            return;
        }

        scrollViewer.AttachedToVisualTree += OnAttachedToVisualTree;
        scrollViewer.DetachedFromVisualTree += OnDetachedFromVisualTree;
        scrollViewer.SetValue(IsLifecycleAttachedProperty, true);
    }

    private static void DetachLifecycle(ScrollViewer scrollViewer)
    {
        if (!GetIsLifecycleAttached(scrollViewer))
        {
            return;
        }

        scrollViewer.AttachedToVisualTree -= OnAttachedToVisualTree;
        scrollViewer.DetachedFromVisualTree -= OnDetachedFromVisualTree;
        scrollViewer.ClearValue(IsLifecycleAttachedProperty);
    }

    private static SmoothScrollState? GetState(ScrollViewer scrollViewer)
    {
        return scrollViewer.GetValue(StateProperty);
    }

    private static void SetState(ScrollViewer scrollViewer, SmoothScrollState state)
    {
        scrollViewer.SetValue(StateProperty, state);
    }

    private static bool GetIsLifecycleAttached(ScrollViewer scrollViewer)
    {
        return scrollViewer.GetValue(IsLifecycleAttachedProperty);
    }

    private static bool HasScrollableDescendantSource(ScrollViewer scrollViewer, PointerWheelEventArgs e)
    {
        if (e.Source is not Visual source)
        {
            return false;
        }

        Visual? current = source;

        while (current is not null && !ReferenceEquals(current, scrollViewer))
        {
            if (current is ScrollViewer descendant
                && CanHandleWheel(descendant, e.Delta))
            {
                return true;
            }

            current = Avalonia.VisualTree.VisualExtensions.GetVisualParent(current);
        }

        return false;
    }

    private static bool CanHandleWheel(ScrollViewer scrollViewer, Vector delta)
    {
        SmoothScrollState? state = GetState(scrollViewer);
        double multiplier = GetWheelMultiplier(scrollViewer);

        if (state is not null)
        {
            return SmoothScrollTargetCalculator.TryCalculateTargetOffset(
                scrollViewer,
                state,
                delta,
                multiplier,
                out Vector _);
        }

        return SmoothScrollTargetCalculator.TryCalculateTargetOffset(
            scrollViewer,
            scrollViewer.Offset,
            delta,
            multiplier,
            out Vector _);
    }

    private static void OnIsEnabledChanged(ScrollViewer scrollViewer, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true)
        {
            EnsureLifecycleAttached(scrollViewer);
            EnsureAttached(scrollViewer);
            return;
        }

        DetachState(scrollViewer);
        DetachLifecycle(scrollViewer);
    }

    private static void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer
            || e.Handled
            || !GetIsEnabled(scrollViewer))
        {
            return;
        }

        if (e.Source is Visual source && !ReferenceEquals(source, scrollViewer)
            && !Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(source).Contains(scrollViewer))
        {
            return;
        }

        if (HasScrollableDescendantSource(scrollViewer, e))
        {
            return;
        }

        SmoothScrollState? state = GetState(scrollViewer);

        if (state is null)
        {
            return;
        }

        double multiplier = GetWheelMultiplier(scrollViewer);

        if (!SmoothScrollTargetCalculator.TryCalculateTargetOffset(
            scrollViewer,
            state,
            e.Delta,
            multiplier,
            out Vector targetOffset))
        {
            return;
        }

        state.Start(targetOffset, GetDuration(scrollViewer));
        e.Handled = true;
    }

    private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer && GetIsEnabled(scrollViewer))
        {
            EnsureAttached(scrollViewer);
        }
    }

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            DetachState(scrollViewer);
        }
    }
}
