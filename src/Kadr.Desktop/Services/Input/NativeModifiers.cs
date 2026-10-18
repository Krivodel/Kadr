using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Settings;

namespace Kadr.Desktop.Services.Input;

internal static class NativeModifiers
{
    internal static ShortcutModifiers Get()
    {
        ShortcutModifiers modifiers = ShortcutModifiers.None;
        if (NativeDesktop.IsKeyDown(0x11))
        {
            modifiers |= ShortcutModifiers.Control;
        }
        if (NativeDesktop.IsKeyDown(0x12))
        {
            modifiers |= ShortcutModifiers.Alt;
        }
        if (NativeDesktop.IsKeyDown(0x10))
        {
            modifiers |= ShortcutModifiers.Shift;
        }
        if (NativeDesktop.IsKeyDown(0x5b) || NativeDesktop.IsKeyDown(0x5c))
        {
            modifiers |= ShortcutModifiers.Windows;
        }
        return modifiers;
    }
}
