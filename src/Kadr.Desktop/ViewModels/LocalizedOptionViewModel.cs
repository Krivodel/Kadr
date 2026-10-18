using CommunityToolkit.Mvvm.ComponentModel;

using Kadr.Desktop.Resources;

namespace Kadr.Desktop.ViewModels;

public sealed class LocalizedOptionViewModel(string key) : ObservableObject
{
    public string Name => UiText.Get(key);

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Name));
    }
}
