namespace Kadr.Desktop.Services.Geometry;

public static class ColorMath
{
    public static uint FromHsl(double hue, double saturation, double lightness, byte alpha)
    {
        double value = lightness + saturation * Math.Min(lightness, 1 - lightness);
        return FromHsv(hue, value == 0 ? 0 : 2 * (1 - lightness / value), value, alpha);
    }

    public static (double Hue, double Saturation, double Lightness) ToHsl(uint color)
    {
        (double hue, double saturation, double value) = ToHsv(color);
        double lightness = value * (1 - saturation / 2);
        double divisor = Math.Min(lightness, 1 - lightness);
        return (hue, divisor == 0 ? 0 : (value - lightness) / divisor, lightness);
    }
    public static string ToRgbaHex(uint color)
    {
        return $"#{color & 0xffffff:X6}{color >> 24:X2}";
    }

    public static bool TryParseRgbaHex(string? text, out uint color)
    {
        string value = text?.Trim().TrimStart('#') ?? string.Empty;

        if (((value.Length == 6) || (value.Length == 8)) && uint.TryParse(value,
                System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint parsed))
        {
            color = value.Length == 6 ? 0xff000000 | parsed : (parsed >> 8) | (parsed << 24);
            return true;
        }

        color = 0;
        return false;
    }

    public static uint FromHsv(double hue, double saturation, double value, byte alpha)
    {
        double chroma = value * saturation;
        double secondary = chroma * (1 - Math.Abs(((hue / 60) % 2) - 1));
        (double red, double green, double blue) = ((int)(hue / 60) % 6) switch
        {
            0 => (chroma, secondary, 0d),
            1 => (secondary, chroma, 0d),
            2 => (0d, chroma, secondary),
            3 => (0d, secondary, chroma),
            4 => (secondary, 0d, chroma),
            _ => (chroma, 0d, secondary)
        };
        double minimum = value - chroma;
        return ((uint)alpha << 24) | ((uint)Math.Round((red + minimum) * 255) << 16)
            | ((uint)Math.Round((green + minimum) * 255) << 8) | (uint)Math.Round((blue + minimum) * 255);
    }

    public static (double Hue, double Saturation, double Value) ToHsv(uint color)
    {
        double red = ((color >> 16) & 255) / 255d;
        double green = ((color >> 8) & 255) / 255d;
        double blue = (color & 255) / 255d;
        double maximum = Math.Max(red, Math.Max(green, blue));
        double minimum = Math.Min(red, Math.Min(green, blue));
        double delta = maximum - minimum;
        double hue = delta == 0 ? 0 : maximum == red ? 60 * (((green - blue) / delta) % 6)
            : maximum == green ? 60 * (((blue - red) / delta) + 2) : 60 * (((red - green) / delta) + 4);
        return (hue < 0 ? hue + 360 : hue, maximum == 0 ? 0 : delta / maximum, maximum);
    }
}
