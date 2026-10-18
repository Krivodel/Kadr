using Avalonia.Controls;
using Avalonia.Platform;

namespace Kadr.Desktop.Views;

internal static class WindowSizing
{
    internal static void FitToScreen(Window window)
    {
        Screen? screen = window.Screens.ScreenFromWindow(window);

        if (screen is not null)
        {
            double maximumWidth = Math.Max(200, (screen.WorkingArea.Width / window.RenderScaling) - 24);
            double maximumHeight = Math.Max(200, (screen.WorkingArea.Height / window.RenderScaling) - 24);
            window.MinWidth = Math.Min(window.MinWidth, maximumWidth);
            window.MinHeight = Math.Min(window.MinHeight, maximumHeight);
            window.Width = Math.Min(window.Width, maximumWidth);
            window.Height = Math.Min(window.Height, maximumHeight);
        }
    }
}
