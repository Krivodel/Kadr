namespace Kadr.Desktop.ViewModels;

public sealed class ColorChoiceEventArgs(uint? color) : EventArgs
{
    public uint? Color { get; } = color;
}
