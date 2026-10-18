using Avalonia;
using Avalonia.Media;

namespace Kadr.Desktop.Controls;

internal static class ColorBrushes
{
    private static readonly IBrush CheckerBackground = new SolidColorBrush(Color.Parse("#586783")).ToImmutable();
    private static readonly IBrush CheckerForeground = new SolidColorBrush(Color.Parse("#8996AC")).ToImmutable();

    internal static void DrawTransparencyGrid(DrawingContext context, Rect area)
    {
        const int CellSize = 6;
        context.FillRectangle(CheckerBackground, area);
        for (int row = 0; row * CellSize < area.Height; row++)
        {
            for (int column = 0; column * CellSize < area.Width; column++)
            {
                if ((row + column) % 2 == 0)
                {
                    context.FillRectangle(CheckerForeground, new Rect(area.X + column * CellSize, area.Y + row * CellSize, CellSize, CellSize));
                }
            }
        }
    }

    internal static GradientStops CreateHueStops()
    {
        return new GradientStops
        {
            new(Colors.Red, 0), new(Colors.Yellow, 1d / 6), new(Colors.Lime, 2d / 6),
            new(Colors.Cyan, 0.5), new(Colors.Blue, 4d / 6), new(Colors.Magenta, 5d / 6), new(Colors.Red, 1)
        };
    }

    internal static LinearGradientBrush CreateGradient(Color start, Color end)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = new GradientStops { new(start, 0), new(end, 1) }
        };
    }
}
