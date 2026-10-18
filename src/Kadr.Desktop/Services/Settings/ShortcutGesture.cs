namespace Kadr.Desktop.Services.Settings;

public sealed record ShortcutGesture(int Key, ShortcutModifiers Modifiers)
{
    public static ShortcutGesture Empty { get; } = new(0, ShortcutModifiers.None);
    public bool IsEmpty => Key == 0;
    public string DisplayName => IsEmpty ? string.Empty : string.Join(" + ", GetParts());

    private IEnumerable<string> GetParts()
    {
        if (Modifiers.HasFlag(ShortcutModifiers.Control))
        {
            yield return "Ctrl";
        }
        if (Modifiers.HasFlag(ShortcutModifiers.Alt))
        {
            yield return "Alt";
        }
        if (Modifiers.HasFlag(ShortcutModifiers.Shift))
        {
            yield return "Shift";
        }
        if (Modifiers.HasFlag(ShortcutModifiers.Windows))
        {
            yield return "Win";
        }
        yield return ShortcutKeyNames.GetName(Key);
    }
}
