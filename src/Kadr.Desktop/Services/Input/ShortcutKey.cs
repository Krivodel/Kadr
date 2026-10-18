using Kadr.Desktop.Services.Settings;

namespace Kadr.Desktop.Services.Input;

internal static class ShortcutKey
{
    internal static ShortcutModifiers GetModifier(int key)
    {
        return key switch
        {
            0x10 or 0xa0 or 0xa1 => ShortcutModifiers.Shift,
            0x11 or 0xa2 or 0xa3 => ShortcutModifiers.Control,
            0x12 or 0xa4 or 0xa5 => ShortcutModifiers.Alt,
            0x5b or 0x5c => ShortcutModifiers.Windows,
            _ => ShortcutModifiers.None
        };
    }

    internal static int Normalize(int key)
    {
        return key switch
        {
            0xa0 or 0xa1 => 0x10,
            0xa2 or 0xa3 => 0x11,
            0xa4 or 0xa5 => 0x12,
            _ => key
        };
    }

    internal static ShortcutGesture Normalize(ShortcutGesture gesture)
    {
        return new ShortcutGesture(Normalize(gesture.Key), gesture.Modifiers & ~GetModifier(gesture.Key));
    }
}
