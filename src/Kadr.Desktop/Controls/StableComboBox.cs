using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Kadr.Desktop.Controls;

public sealed class StableComboBox : ComboBox
{
    protected override Type StyleKeyOverride => typeof(ComboBox);

    public StableComboBox()
    {
        AddHandler(RequestBringIntoViewEvent, OnBringIntoViewRequested);
    }

    private void OnBringIntoViewRequested(object? sender, RequestBringIntoViewEventArgs arguments)
    {
        if (arguments.TargetObject is Avalonia.Visual target && !ReferenceEquals(target, this)
            && !target.GetVisualAncestors().Contains(this))
        {
            arguments.Handled = true;
        }
    }
}
