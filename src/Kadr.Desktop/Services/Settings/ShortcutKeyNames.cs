using System.Runtime.InteropServices;
using System.Text;

namespace Kadr.Desktop.Services.Settings;

internal static class ShortcutKeyNames
{
    private static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
    {
        [2] = "RMB", [4] = "MMB", [8] = "Backspace", [9] = "Tab", [12] = "Clear", [13] = "Enter",
        [16] = "Shift", [17] = "Ctrl", [18] = "Alt", [19] = "Pause", [20] = "CapsLock", [27] = "Esc",
        [32] = "Space", [33] = "PageUp", [34] = "PageDown", [35] = "End", [36] = "Home", [37] = "Left",
        [38] = "Up", [39] = "Right", [40] = "Down", [44] = "PrintScreen", [45] = "Insert", [46] = "Delete",
        [91] = "Left Win", [92] = "Right Win", [93] = "Menu", [106] = "Num *", [107] = "Num +",
        [109] = "Num -", [110] = "Num .", [111] = "Num /", [144] = "NumLock", [145] = "ScrollLock",
        [186] = ";", [187] = "=", [188] = ",", [189] = "-", [190] = ".", [191] = "/", [192] = "~",
        [219] = "[", [220] = "\\", [221] = "]", [222] = "'", [226] = "OEM 102"
    };

    internal static string GetName(int key)
    {
        if (Names.TryGetValue(key, out string? name))
        {
            return name;
        }
        if ((key >= 0x30 && key <= 0x39) || (key >= 0x41 && key <= 0x5a))
        {
            return ((char)key).ToString();
        }
        if (key >= 0x70 && key <= 0x87)
        {
            return $"F{key - 0x6f}";
        }
        if (key >= 0x60 && key <= 0x69)
        {
            return $"Num {key - 0x60}";
        }
        uint scan = MapVirtualKeyW((uint)key, 0);
        StringBuilder buffer = new(128);
        return GetKeyNameTextW((int)(scan << 16), buffer, buffer.Capacity) > 0 ? buffer.ToString() : $"Key {key:X2}";
    }

    [DllImport("user32.dll")] private static extern uint MapVirtualKeyW(uint code, uint type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetKeyNameTextW(int code, StringBuilder text, int size);
}
