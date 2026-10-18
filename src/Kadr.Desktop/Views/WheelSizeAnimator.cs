using System.Diagnostics;

using Avalonia.Controls;

using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.ViewModels;

namespace Kadr.Desktop.Views;

internal sealed class WheelSizeAnimator : IDisposable
{
    private readonly Window _window;
    private readonly EditorViewModel _model;
    private readonly Action _refresh;
    private readonly WheelSizeTransition _transition = new();
    private readonly WheelSizeScale _scale = new();
    private object _group = new();
    private object? _target;
    private TimeSpan? _lastInput;
    private bool _framePending;
    private bool _disposed;

    internal WheelSizeAnimator(Window window, EditorViewModel model, Action refresh)
    {
        _window = window;
        _model = model;
        _refresh = refresh;
    }

    internal void Add(double delta)
    {
        if (_disposed || !double.IsFinite(delta) || delta == 0)
        {
            return;
        }
        EditorInteraction interaction = _model.Interaction;
        TimeSpan now = Stopwatch.GetElapsedTime(0);
        object? target = interaction.SizeTarget;
        if (_lastInput is not TimeSpan last || now - last > WheelSizeTransition.BurstTimeout || !Equals(_target, target))
        {
            Finish();
            _group = new object();
            _scale.Reset();
        }
        double previousTarget = _transition.IsActive ? _transition.Target : interaction.SizeValue;
        _target = target;
        _lastInput = now;
        _transition.Begin(interaction.SizeValue, _scale.GetAcceleratedTarget(previousTarget, delta, interaction.MinimumSize, interaction.MaximumSize, now), now);
        ScheduleFrame();
    }

    internal void Finish()
    {
        if (!_disposed && _transition.IsActive)
        {
            double target = _transition.Finish();
            if (Equals(_target, _model.Interaction.SizeTarget))
            {
                Apply(target);
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _transition.Finish();
    }

    private void ScheduleFrame()
    {
        if (!_framePending && _transition.IsActive)
        {
            _framePending = true;
            _window.RequestAnimationFrame(OnFrame);
        }
    }

    private void Apply(double value)
    {
        double delta = value - _model.Interaction.SizeValue;
        if (Math.Abs(delta) > 0)
        {
            _model.AdjustDrawingSize(delta, _group);
            _refresh();
        }
    }

    private void OnFrame(TimeSpan timestamp)
    {
        _framePending = false;
        if (_disposed || !_transition.IsActive)
        {
            return;
        }
        if (!Equals(_target, _model.Interaction.SizeTarget))
        {
            _transition.Finish();
            return;
        }
        Apply(_transition.Advance(Stopwatch.GetElapsedTime(0)));
        ScheduleFrame();
    }
}
