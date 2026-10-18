using Krivodeling.Localization.Avalonia;

namespace Kadr.Desktop.Resources;

public static class UiText
{
    public static BuiltInLocalizationCatalog Catalog { get; } = BuiltInLocalizationCatalog.FromAssemblies(typeof(UiText).Assembly);

    public static string Get(string key)
    {
        LocalizationText.InitializeIfNeeded(Catalog);
        return LocalizationText.Get(key);
    }

    public static string Format(string key, params object?[] arguments)
    {
        LocalizationText.InitializeIfNeeded(Catalog);
        return LocalizationText.Format(key, arguments);
    }
}
