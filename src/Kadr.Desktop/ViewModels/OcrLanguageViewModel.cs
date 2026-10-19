using CommunityToolkit.Mvvm.ComponentModel;

using Kadr.Desktop.Resources;

namespace Kadr.Desktop.ViewModels;

public sealed partial class OcrLanguageViewModel : ObservableObject
{
    public string Code
    {
        get;
    }
    public string Name => _nativeName + UiText.Get("Kadr.InstalledSuffix");

    private readonly string _nativeName;

    [ObservableProperty] private bool _isSelected;

    public OcrLanguageViewModel(string code, bool selected)
    {
        Code = code;
        string name = System.Globalization.CultureInfo.GetCultures(System.Globalization.CultureTypes.NeutralCultures)
            .FirstOrDefault(culture => string.Equals(culture.ThreeLetterISOLanguageName, code, StringComparison.OrdinalIgnoreCase))?.NativeName ?? code;
        _nativeName = name;
        IsSelected = selected;
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Name));
    }
}
