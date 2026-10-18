using Avalonia.Input;
using Avalonia.Win32.Input;

using Kadr.Desktop.Services.Settings;

namespace Kadr.Desktop.Services.Input;

internal static class KeyboardGestures
{
    internal static int GetPressedMouseKey(PointerPressedEventArgs arguments)
    {
        return arguments.GetCurrentPoint(null).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.MiddleButtonPressed => 4,
            PointerUpdateKind.RightButtonPressed => 2,
            _ => 0
        };
    }

    internal static bool Matches(KeyEventArgs arguments, ShortcutGesture shortcut)
    {
        return !shortcut.IsEmpty && ShortcutKey.Normalize(FromKey(arguments)) == shortcut;
    }

    internal static bool IsHeld(ShortcutGesture shortcut, Func<int, bool> isKeyDown)
    {
        ShortcutModifiers modifier = ShortcutKey.GetModifier(shortcut.Key);
        return !shortcut.IsEmpty && isKeyDown(shortcut.Key)
            && ((modifier != ShortcutModifiers.None && shortcut.Modifiers == ShortcutModifiers.None)
                || (NativeModifiers.Get(isKeyDown) & ~modifier) == shortcut.Modifiers);
    }

    internal static bool Matches(KeyEventArgs arguments, AppSettings settings, EditorAction action)
    {
        return ShortcutCatalog.GetBindings(settings, action).Any(shortcut => Matches(arguments, shortcut));
    }

    internal static ShortcutDefinition? FindMouse(AppSettings settings, KeyModifiers modifiers, int key = 4)
    {
        return ShortcutCatalog.Definitions.FirstOrDefault(definition => definition.Action != EditorAction.Capture
            && ShortcutCatalog.GetBindings(settings, definition.Action).Any(shortcut => MatchesMouse(modifiers, shortcut, key)));
    }

    internal static bool MatchesMouse(KeyModifiers modifiers, ShortcutGesture shortcut, int key = 4)
    {
        return MouseShortcut.IsMouseKey(key) && shortcut.Key == key && shortcut.Modifiers == GetModifiers(modifiers);
    }

    internal static ShortcutGesture FromKey(KeyEventArgs arguments)
    {
        return new ShortcutGesture(KeyInterop.VirtualKeyFromKey(arguments.Key), GetModifiers(arguments.KeyModifiers));
    }

    private static ShortcutModifiers GetModifiers(KeyModifiers modifiers)
    {
        ShortcutModifiers result = ShortcutModifiers.None;

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            result |= ShortcutModifiers.Control;
        }
        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            result |= ShortcutModifiers.Alt;
        }
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            result |= ShortcutModifiers.Shift;
        }
        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            result |= ShortcutModifiers.Windows;
        }
        return result;
    }
}
