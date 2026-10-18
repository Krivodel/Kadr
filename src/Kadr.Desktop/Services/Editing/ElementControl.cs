using Kadr.Desktop.Services.Geometry;

namespace Kadr.Desktop.Services.Editing;

public readonly record struct ElementControl(ElementControlKind Kind, PixelPoint Position, ResizeHandle Edges = ResizeHandle.None);
