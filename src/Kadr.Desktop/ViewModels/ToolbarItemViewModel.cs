using CommunityToolkit.Mvvm.ComponentModel;

using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Settings;

namespace Kadr.Desktop.ViewModels;

public sealed partial class ToolbarItemViewModel(ToolbarItemDefinition definition) : ObservableObject
{
    public ToolbarItemId Id => definition.Id;
    public string Name => UiText.Get(definition.NameKey);

    [ObservableProperty] private bool _isVisible = true;

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Name));
    }
}
