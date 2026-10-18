namespace Kadr.Desktop.Services.Editing;

public static class DrawingDefaults
{
    public const uint Color = 0xffff0000;
    public const double Thickness = 14.143375000000002;
    public const double MinimumThickness = 1;
    public const double MaximumThickness = 128;
    public const double BlurStrength = 7;
    public const double MinimumBlurStrength = 1;
    public const double MaximumBlurStrength = 128;
    public const string TypefaceName = "Segoe UI";
    public const double TextLineSpacing = 1.3;
    public const double MinimumTextSize = 12;
    public const double MaximumTextSize = 256;
    public const byte MarkerOpacity = 96;
    public const double MarkerThicknessMultiplier = 10;

    public static double GetStrokeThickness(DrawingToolId tool, double thickness)
    {
        return tool == DrawingToolId.Marker ? thickness * MarkerThicknessMultiplier : thickness;
    }

    public static double GetLabelSize(double thickness)
    {
        double progress = (Math.Clamp(thickness, MinimumThickness, MaximumThickness) - MinimumThickness)
            / (MaximumThickness - MinimumThickness);
        return MinimumTextSize + progress * (MaximumTextSize - MinimumTextSize);
    }

    public static double GetTextLineHeight(double thickness)
    {
        return GetLabelSize(thickness) * TextLineSpacing;
    }

    public static double GetNumberSize(double thickness)
    {
        return Math.Max(24, thickness * 2);
    }
}
