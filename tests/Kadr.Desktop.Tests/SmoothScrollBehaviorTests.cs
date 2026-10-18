using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;

using Kadr.Desktop.Controls.Scrolling;
using Xunit;

namespace Kadr.Desktop.Tests;

[Collection("Avalonia UI")]
public sealed class SmoothScrollBehaviorTests
{
    [Fact]
    public async Task Wheel_RepeatedInputAndManualOffset_SmoothsAccumulatesAndStopsOnExternalChange()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            ScrollViewer viewer = CreateViewer(new Border { Height = 2000, Background = Brushes.Gray });
            Window window = new() { Width = 400, Height = 500, Content = viewer };
            window.Show();
            await UiTestSession.PumpFramesAsync(50);

            window.MouseWheel(new Point(100, 100), new Vector(0, -1));
            window.MouseWheel(new Point(100, 100), new Vector(0, -1));
            await UiTestSession.PumpFramesAsync(35);
            Assert.InRange(viewer.Offset.Y, 0.01, 191.99);
            await UiTestSession.PumpFramesAsync(650);
            Assert.InRange(viewer.Offset.Y, 191.98, 192.02);
            window.MouseWheel(new Point(100, 100), new Vector(0, -1));
            await UiTestSession.PumpFramesAsync(25);
            viewer.Offset = new Vector(0, 350);
            await UiTestSession.PumpFramesAsync(300);
            Assert.Equal(350, viewer.Offset.Y);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Wheel_AtBoundaryThenReverse_ClampsTargetAndResetCancelsPendingFrames()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            ScrollViewer viewer = CreateViewer(new Border { Height = 2000, Background = Brushes.Gray });
            Window window = new() { Width = 400, Height = 500, Content = viewer };
            window.Show();
            await UiTestSession.PumpFramesAsync(50);
            double maximum = viewer.Extent.Height - viewer.Viewport.Height;
            viewer.Offset = new Vector(0, maximum - 10);

            window.MouseWheel(new Point(100, 100), new Vector(0, -20));
            await UiTestSession.PumpFramesAsync(25);
            window.MouseWheel(new Point(100, 100), new Vector(0, 1));
            await UiTestSession.PumpFramesAsync(650);

            Assert.InRange(viewer.Offset.Y, maximum - 96.02, maximum - 95.98);
            window.MouseWheel(new Point(100, 100), new Vector(0, -1));
            SmoothScrollBehavior.ResetOffset(viewer);
            await UiTestSession.PumpFramesAsync(300);
            Assert.Equal(0, viewer.Offset.Y);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Wheel_OverNestedScrollableControl_LeavesParentAtRest()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            ScrollViewer child = CreateViewer(new Border { Height = 1000, Background = Brushes.Gray });
            child.Height = 180;
            StackPanel content = new();
            content.Children.Add(child);
            content.Children.Add(new Border { Height = 1000 });
            ScrollViewer parent = CreateViewer(content);
            Window window = new() { Width = 400, Height = 500, Content = parent };
            window.Show();
            await UiTestSession.PumpFramesAsync(50);

            window.MouseWheel(new Point(100, 100), new Vector(0, -1));
            await UiTestSession.PumpFramesAsync(650);

            Assert.InRange(child.Offset.Y, 95.98, 96.02);
            Assert.Equal(0, parent.Offset.Y);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static ScrollViewer CreateViewer(Control content)
    {
        ScrollViewer viewer = new()
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        SmoothScrollBehavior.SetIsEnabled(viewer, true);
        return viewer;
    }
}
