namespace Kadr.Desktop.Services.Geometry;

[Flags]
public enum ResizeHandle
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8
}
