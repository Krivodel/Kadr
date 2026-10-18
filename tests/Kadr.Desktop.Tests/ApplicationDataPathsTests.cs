using Kadr.Desktop.Services;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class ApplicationDataPathsTests
{
    [Theory]
    [InlineData("--settings-probe")]
    [InlineData("--editor-probe")]
    public void FromArguments_UiProbe_KeepsEveryDataPathInsideReportProfile(string command)
    {
        string report = Path.Combine(Path.GetTempPath(), "Kadr-profile-test", "Проверка.json");

        ApplicationDataPaths paths = ApplicationDataPaths.FromArguments(new string[] { command, report });

        Assert.Equal(Path.GetFullPath(report) + ".data", paths.DirectoryPath);
        Assert.NotEqual(ApplicationDataPaths.Default.DirectoryPath, paths.DirectoryPath);
        Assert.All(new string[] { paths.SettingsFile, paths.LocalizationsDirectory, paths.LogsDirectory,
            paths.OcrDirectory, paths.UpdateSessionDirectory }, path =>
                Assert.StartsWith(paths.DirectoryPath + Path.DirectorySeparatorChar, path, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("--silent")]
    [InlineData("--smoke-test")]
    public void FromArguments_NormalLaunch_PreservesUserProfileLocation(string argument)
    {
        ApplicationDataPaths paths = ApplicationDataPaths.FromArguments(new string[] { argument });

        Assert.Equal(ApplicationDataPaths.Default, paths);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kadr", "settings.json"), paths.SettingsFile);
    }
}
