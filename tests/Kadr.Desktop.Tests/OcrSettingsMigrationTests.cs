using System.Text.Json;

using Kadr.Desktop.Services.Settings;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class OcrSettingsMigrationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Read_RemovedBackend_PreservesPreferencesAndRemovesObsoleteSetting(int oldBackend)
    {
        SettingsDocument document = new(JsonSerializer.Serialize(new
        {
            OcrBackend = oldBackend, Thickness = 37, Color = 0xff00ff00u, ShowCursor = true,
            OcrLanguages = new string[] { "eng", "fra", "rus" }, UnrelatedFutureSetting = "preserved"
        }));

        AppSettings settings = document.Read();
        string saved = document.Serialize(settings);

        using JsonDocument json = JsonDocument.Parse(saved);
        Assert.False(json.RootElement.TryGetProperty("OcrBackend", out _));
        Assert.Equal(37, settings.Thickness);
        Assert.Equal(0xff00ff00u, settings.Color);
        Assert.True(settings.ShowCursor);
        Assert.Equal(new string[] { "eng", "rus" }, settings.OcrLanguages);
        Assert.Contains("preserved", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_UnsupportedPreviousLanguages_UsesBundledDefaultLanguages()
    {
        SettingsDocument document = new("{\"OcrLanguages\":[\"fra\"],\"Thickness\":23}");

        AppSettings settings = document.Read();

        Assert.Equal(new string[] { "rus", "eng" }, settings.OcrLanguages);
        Assert.Equal(23, settings.Thickness);
    }
}
