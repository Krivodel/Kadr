using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.ViewModels;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class ColorPickerViewModelTests
{
    [Theory]
    [InlineData(0xff3b82f6u)]
    [InlineData(0x12345678u)]
    [InlineData(0xffffffffu)]
    [InlineData(0xff000000u)]
    [InlineData(0x00808080u)]
    [InlineData(0xffff0000u)]
    public void Hsl_ConvertAndRestore_PreservesAllChannels(uint original)
    {
        (double hue, double saturation, double lightness) = ColorMath.ToHsl(original);

        uint restored = ColorMath.FromHsl(hue, saturation, lightness, (byte)(original >> 24));

        Assert.Equal(original, restored);
    }

    [Fact]
    public void ColorPicker_InvalidHex_PreservesColorAndBlocksAcceptanceUntilCorrected()
    {
        ColorPickerViewModel model = new(0x803b82f6);
        List<uint?> results = [];
        model.CloseRequested += (sender, arguments) => results.Add(arguments.Color);

        model.Hex = "invalid";
        model.AcceptCommand.Execute(null);
        Assert.True(model.HasHexError);
        Assert.Equal(0x803b82f6u, model.ColorValue);
        Assert.Empty(results);
        model.Hex = "#00FF0040";
        model.AcceptCommand.Execute(null);

        Assert.False(model.HasHexError);
        Assert.Equal(0x4000ff00u, Assert.Single(results));
        Assert.Equal(64, model.Alpha);
        Assert.Equal(255, model.Green);
    }

    [Fact]
    public void ColorPicker_HueOnWhiteAndAlphaOnBlack_PreserveLatentHsvSelection()
    {
        ColorPickerViewModel model = new(0xffffffff);
        model.Hue = 240;
        model.Saturation = 100;
        Assert.Equal(0xff0000ffu, model.ColorValue);

        model.SetHsv(120, 0.75, 0);
        model.Alpha = 128;

        Assert.Equal(120, model.Hue);
        Assert.Equal(75, model.Saturation);
        Assert.Equal(0x80000000u, model.ColorValue);
        model.RestoreOriginalCommand.Execute(null);
        Assert.Equal(0xffffffffu, model.ColorValue);
    }

    [Fact]
    public void ColorPicker_HslAndRgbEdits_UpdateSharedColorAndKeepTransparency()
    {
        ColorPickerViewModel model = new(0x803b82f6);
        model.Lightness = 50;
        model.HslSaturation = 100;
        model.Hue = 120;
        Assert.Equal(0x8000ff00u, model.ColorValue);

        model.Red = 255;
        model.Blue = 255;

        Assert.Equal(0x80ffffffu, model.ColorValue);
        model.CancelCommand.Execute(null);
    }

    [Fact]
    public void WheelTransition_ContinuousRetargetWithoutIdleFrame_AdvancesBeforeInputStops()
    {
        WheelSizeTransition transition = new();
        transition.Begin(0, 100, TimeSpan.Zero);

        for (int millisecond = 1; millisecond <= 100; millisecond++)
        {
            transition.Begin(0, 100, TimeSpan.FromMilliseconds(millisecond));
        }

        Assert.InRange(transition.Value, 94, 99);
        Assert.Equal(100, transition.Advance(TimeSpan.FromMilliseconds(290)));
    }
}
