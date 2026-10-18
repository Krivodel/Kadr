using Kadr.Desktop.Services;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class DistributionNoticesTests
{
    [Fact]
    public async Task ExportAsync_StandaloneResources_PreservesLicenseTextsWithoutAdjacentFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-notices-" + Guid.NewGuid().ToString("N"));
        try
        {
            await DistributionNotices.ExportAsync(directory, CancellationToken.None);

            string notices = await File.ReadAllTextAsync(Path.Combine(directory, "THIRD-PARTY-NOTICES.md"));
            Assert.Contains("Tesseract", notices);
            Assert.Contains("Distributable Code for Microsoft Visual Studio 2022", notices);
            Assert.Contains("https://aka.ms/vs/17/redist.txt", notices);
            Assert.Contains("Copyright", await File.ReadAllTextAsync(Path.Combine(directory, "Lang.Avalonia-MIT.txt")));
            Assert.False(File.Exists(Path.Combine(directory, "Microsoft-Visual-Studio-Redist.txt")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
