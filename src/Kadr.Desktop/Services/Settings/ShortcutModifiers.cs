namespace Kadr.Desktop.Services.Settings;

[Flags]
public enum ShortcutModifiers : uint
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}
