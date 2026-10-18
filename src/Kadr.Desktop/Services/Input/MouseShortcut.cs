namespace Kadr.Desktop.Services.Input;

internal static class MouseShortcut
{
    internal static bool IsMouseKey(int key)
    {
        return key is 2 or 4;
    }

    internal static int GetPressedKey(nint message)
    {
        return message switch { 0x204 => 2, 0x207 => 4, _ => 0 };
    }

    internal static int GetReleasedKey(nint message)
    {
        return message switch { 0x205 => 2, 0x208 => 4, _ => 0 };
    }
}
