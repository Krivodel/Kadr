namespace Kadr.Desktop.Services;

internal sealed record ApplicationDataPaths(string DirectoryPath)
{
    internal static ApplicationDataPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kadr"));

    internal string SettingsFile => Path.Combine(DirectoryPath, "settings.json");
    internal string LocalizationsDirectory => Path.Combine(DirectoryPath, "Localizations");
    internal string OcrDirectory => Path.Combine(DirectoryPath, "tessdata");
    internal string UpdateSessionDirectory => Path.Combine(DirectoryPath, "UpdateSession");
    internal string LogsDirectory => Path.Combine(DirectoryPath, "logs");

    internal static ApplicationDataPaths FromArguments(string[] arguments)
    {
        if (arguments.Length > 1 && arguments[0] is "--settings-probe" or "--editor-probe")
        {
            return ForProbe(arguments[1]);
        }

        return Default;
    }

    internal static ApplicationDataPaths ForProbe(string reportPath)
    {
        return new ApplicationDataPaths(Path.GetFullPath(reportPath) + ".data");
    }
}
