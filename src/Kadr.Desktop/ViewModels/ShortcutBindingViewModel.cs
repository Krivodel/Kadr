using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Settings;

namespace Kadr.Desktop.ViewModels;

public sealed partial class ShortcutBindingViewModel : ObservableObject
{
    public EditorAction Action
    {
        get;
    }
    public string Name => _definition.Name;
    public string DisplayName => Gesture.DisplayName;

    public string AlternativeDisplayName => AlternativeGesture?.DisplayName ?? string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlternativeDisplayName))]
    [NotifyCanExecuteChangedFor(nameof(ClearAlternativeCommand))]
    private ShortcutGesture? _alternativeGesture;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private ShortcutGesture _gesture;
    private readonly ShortcutDefinition _definition;

    public ShortcutBindingViewModel(ShortcutDefinition definition, ShortcutGesture gesture, ShortcutGesture? alternative = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(gesture);
        Action = definition.Action;
        _definition = definition;
        _gesture = gesture;
        _alternativeGesture = alternative;
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(AlternativeDisplayName));
    }
    [RelayCommand(CanExecute = nameof(CanClearAlternative))]
    private void ClearAlternative()
    {
        AlternativeGesture = null;
    }

    private bool CanClearAlternative()
    {
        return AlternativeGesture is not null;
    }
}

