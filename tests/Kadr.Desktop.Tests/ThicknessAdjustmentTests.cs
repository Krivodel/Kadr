using Kadr.Desktop.Services.Editing;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class ThicknessAdjustmentTests
{
    [Fact]
    public void Rebase_PointerRestoredAtScreenEdge_ContinuesFineAdjustmentToMinimum()
    {
        ThicknessAdjustment adjustment = new(8, 100);
        adjustment.Update(4, true);
        Assert.Equal(4, adjustment.Value);
        adjustment.Rebase(100);

        adjustment.Update(4, true);

        Assert.Equal(1, adjustment.Value);
        adjustment.Update(28, true);
        Assert.Equal(2, adjustment.Value);
    }

    [Theory]
    [InlineData(1, -800, 8, 2)]
    [InlineData(128, 800, -8, 127)]
    public void Update_BeyondLimitThenReverse_ChangesImmediately(double initial, double overshoot, double reverse, double expected)
    {
        ThicknessAdjustment adjustment = new(initial, 0);
        adjustment.Update(overshoot, false);

        adjustment.Update(overshoot + reverse, false);

        Assert.Equal(expected, adjustment.Value);
    }

    [Fact]
    public void Update_PrecisionModifier_UsesThirdSpeedWithoutJumpWhenModifierChanges()
    {
        ThicknessAdjustment adjustment = new(10, 100);
        adjustment.Update(124, false);
        Assert.Equal(13, adjustment.Value);

        adjustment.Update(148, true);
        Assert.Equal(14, adjustment.Value);
        adjustment.Update(148, false);
        Assert.Equal(14, adjustment.Value);
    }
}
