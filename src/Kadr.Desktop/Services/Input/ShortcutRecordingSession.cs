using Kadr.Desktop.Services.Settings;

namespace Kadr.Desktop.Services.Input;

internal sealed class ShortcutRecordingSession
{
    internal double ClearProgress { get; private set; }
    internal bool IsHoldingSpace => _spacePressed is not null;

    private static readonly TimeSpan ClearDuration = TimeSpan.FromMilliseconds(500);
    private readonly Action<ShortcutGesture> _recorded;
    private readonly Action _cleared;
    private readonly Action _clearReleased;
    private ShortcutGesture? _pending;
    private TimeSpan? _spacePressed;
    private bool _completed;

    internal ShortcutRecordingSession(Action<ShortcutGesture> recorded, Action cleared, Action clearReleased)
    {
        _recorded = recorded ?? throw new ArgumentNullException(nameof(recorded));
        _cleared = cleared ?? throw new ArgumentNullException(nameof(cleared));
        _clearReleased = clearReleased ?? throw new ArgumentNullException(nameof(clearReleased));
    }

    internal bool Press(ShortcutGesture gesture, TimeSpan timestamp)
    {
        if (gesture.Key is 0 or 0x10 or 0x11 or 0x12 or 0x5b or 0x5c or >= 0xa0 and <= 0xa5)
        {
            return false;
        }
        if (_completed)
        {
            return true;
        }
        if (gesture == new ShortcutGesture(0x20, ShortcutModifiers.None) && _pending is null)
        {
            _spacePressed = timestamp;
        }
        else if (_pending is not null && _pending.Key != gesture.Key)
        {
            _spacePressed = null;
            ClearProgress = 0;
        }
        _pending ??= gesture;
        return true;
    }

    internal bool Release(int key, TimeSpan timestamp)
    {
        if (_pending is not ShortcutGesture pending || pending.Key != key)
        {
            return false;
        }
        Tick(timestamp);
        _spacePressed = null;
        ClearProgress = 0;
        if (_completed)
        {
            _clearReleased();
        }
        else
        {
            _completed = true;
            _recorded(pending);
        }
        return true;
    }

    internal void Tick(TimeSpan timestamp)
    {
        if (_completed || _spacePressed is not TimeSpan start)
        {
            return;
        }
        ClearProgress = Math.Clamp((timestamp - start).TotalMilliseconds / ClearDuration.TotalMilliseconds, 0, 1);
        if (ClearProgress >= 1)
        {
            _completed = true;
            _cleared();
        }
    }
}
