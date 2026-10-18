using Avalonia.Controls;

namespace Kadr.Desktop.Views;

internal static class FloatingPanelAnimator
{
    internal static void SetVisible(Control panel, bool visible)
    {
        panel.IsHitTestVisible = visible;
        panel.Opacity = visible ? 1 : 0;
    }
}
