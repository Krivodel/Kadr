using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Settings;

namespace Kadr.Desktop.Services.Input;

internal static class NativeModifiers
{
    internal static ShortcutModifiers Get(Func<int, bool>? isKeyDown = null)
    {
        isKeyDown ??= NativeDesktop.IsKeyDown;
        ShortcutModifiers modifiers = ShortcutModifiers.None;
        if (isKeyDown(0x11))
        {
            modifiers |= ShortcutModifiers.Control;
        }
        if (isKeyDown(0x12))
        {
            modifiers |= ShortcutModifiers.Alt;
        }
        if (isKeyDown(0x10))
        {
            modifiers |= ShortcutModifiers.Shift;
        }
        if (isKeyDown(0x5b) || isKeyDown(0x5c))
        {
            modifiers |= ShortcutModifiers.Windows;
        }
        return modifiers;
    }
}
