using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.Services.Export;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task UpdateAsync_BeforeLoad_PreservesExistingPreferencesAndFutureSettings()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-settings-future-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        const string Original = """
            {"Color":4279383126,"Thickness":17,"AutoStart":false,"CopyFormat":1,
             "FutureOption":{"Enabled":true},
             "EditorShortcuts":{"Arrow":{"Key":113,"Modifiers":0},"FutureTool":{"Key":114,"Modifiers":0},"999":null},
             "AlternativeShortcuts":{"FutureTool":{"Key":115,"Modifiers":0}}}
            """;
        await File.WriteAllTextAsync(path, Original);
        SettingsStore store = new(NullLogger<SettingsStore>.Instance, path);
        try
        {
            await store.UpdateAsync(current => current with { LocalizationId = "English" }, CancellationToken.None);
            SettingsStore reloaded = new(NullLogger<SettingsStore>.Instance, path);
            await reloaded.LoadAsync(CancellationToken.None);

            Assert.Equal(0xff123456u, reloaded.Current.Color);
            Assert.Equal(17, reloaded.Current.Thickness);
            Assert.False(reloaded.Current.AutoStart);
            Assert.Equal(ScreenshotFormat.Jpeg, reloaded.Current.CopyFormat);
            Assert.Equal("English", reloaded.Current.LocalizationId);
            Assert.Equal(113, ShortcutCatalog.Get(reloaded.Current, EditorAction.Arrow).Key);
            using JsonDocument original = JsonDocument.Parse(Original);
            using JsonDocument saved = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original.RootElement.GetProperty("FutureOption").GetRawText()),
                JsonNode.Parse(saved.RootElement.GetProperty("FutureOption").GetRawText())));
            foreach (string property in new string[] { "EditorShortcuts", "AlternativeShortcuts" })
            {
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original.RootElement.GetProperty(property).GetProperty("FutureTool").GetRawText()),
                    JsonNode.Parse(saved.RootElement.GetProperty(property).GetProperty("FutureTool").GetRawText())));
            }
            Assert.Equal(JsonValueKind.Null, saved.RootElement.GetProperty("EditorShortcuts").GetProperty("999").ValueKind);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"Thickness\":999}")]
    [InlineData("{\"EditorShortcuts\":null}")]
    public async Task LoadAsync_UnreadableSettings_BlocksOverwriteAndPreservesOriginalFile(string original)
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-protected-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(path, original);
        SettingsStore store = new(NullLogger<SettingsStore>.Instance, path);
        int changes = 0;
        store.Changed += (sender, arguments) => changes++;
        try
        {
            await store.LoadAsync(CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(
                current => current with { LocalizationId = "English" }, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(new AppSettings(), CancellationToken.None));

            Assert.Equal(original, await File.ReadAllTextAsync(path));
            Assert.Equal(0, changes);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task UpdateAsync_RemovingKnownShortcut_PreservesOnlyUnknownEntriesFromOriginal()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-settings-remove-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(path, """{"EditorShortcuts":{"Arrow":{"Key":113,"Modifiers":0},"FutureTool":null}}""");
        SettingsStore store = new(NullLogger<SettingsStore>.Instance, path);
        try
        {
            await store.UpdateAsync(current => current with { EditorShortcuts = new Dictionary<EditorAction, ShortcutGesture>() }, CancellationToken.None);
            using JsonDocument saved = JsonDocument.Parse(await File.ReadAllTextAsync(path));

            JsonElement shortcuts = saved.RootElement.GetProperty("EditorShortcuts");
            Assert.False(shortcuts.TryGetProperty("Arrow", out _));
            Assert.True(shortcuts.TryGetProperty("FutureTool", out _));
            Assert.Equal(ShortcutCatalog.GetDefault(EditorAction.Arrow), ShortcutCatalog.Get(store.Current, EditorAction.Arrow));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Changed_AfterSuccessfulCommit_ExposesNewSettingsAndDoesNotNotifyFailedWrites()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-settings-event-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        SettingsStore store = new(NullLogger<SettingsStore>.Instance, path);
        List<bool> values = [];
        store.Changed += (sender, arguments) => values.Add(store.Current.AutoSelectCreatedElements);
        try
        {
            await store.UpdateAsync(current => current with { AutoSelectCreatedElements = true }, CancellationToken.None);
            Assert.Equal(new bool[] { true }, values);
            await Assert.ThrowsAsync<ArgumentException>(() => store.UpdateAsync(current => current with { Thickness = double.NaN }, CancellationToken.None));

            Assert.Single(values);
            SettingsStore reloaded = new(NullLogger<SettingsStore>.Instance, path);
            await reloaded.LoadAsync(CancellationToken.None);
            Assert.True(reloaded.Current.AutoSelectCreatedElements);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task SaveAsync_AlternativesAndBlurStrength_RoundTripExplicitEmptyOverride()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-alternative-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        SettingsStore store = new(NullLogger<SettingsStore>.Instance, path);
        AppSettings settings = new()
        {
            Thickness = 4,
            BlurStrength = 77,
            AlternativeShortcuts = new Dictionary<EditorAction, ShortcutGesture?>
            {
                [EditorAction.Select] = null,
                [EditorAction.Arrow] = new ShortcutGesture(0x70, ShortcutModifiers.None)
            }
        };

        try
        {
            await store.SaveAsync(settings, CancellationToken.None);
            SettingsStore loaded = new(NullLogger<SettingsStore>.Instance, path);
            await loaded.LoadAsync(CancellationToken.None);

            Assert.Null(ShortcutCatalog.GetAlternative(loaded.Current, EditorAction.Select));
            Assert.Equal(new ShortcutGesture(0x70, ShortcutModifiers.None), ShortcutCatalog.GetAlternative(loaded.Current, EditorAction.Arrow));
            Assert.Equal(77, loaded.Current.BlurStrength);
            Assert.Equal(4, loaded.Current.Thickness);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task LoadAsync_LegacyCaptureConflictsWithNewDefault_PreservesPreferencesForCorrection()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-legacy-conflict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        ShortcutGesture capture = new(0x43, ShortcutModifiers.Control);

        try
        {
            await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(new
            {
                CaptureShortcut = capture,
                Color = 0xff123456u,
                Thickness = 17
            }));
            SettingsStore store = new(NullLogger<SettingsStore>.Instance, path);

            await store.LoadAsync(CancellationToken.None);

            Assert.Equal(capture, store.Current.CaptureShortcut);
            Assert.Equal(0xff123456u, store.Current.Color);
            Assert.Equal(17, store.Current.Thickness);
            Assert.NotNull(ShortcutPolicy.GetError(store.Current));
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData(0x48, 0x43)]
    [InlineData(0x72, 0x72)]
    public async Task LoadAsync_LegacySettings_UpdatesCursorDefaultAndKeepsCustomBinding(int oldKey, int expectedKey)
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");

        try
        {
            string legacy = System.Text.Json.JsonSerializer.Serialize(new
            {
                CursorShortcut = new ShortcutGesture(oldKey, ShortcutModifiers.None),
                Color = 0xff123456u
            });
            await File.WriteAllTextAsync(path, legacy);
            SettingsStore store = new(NullLogger<SettingsStore>.Instance, path);

            await store.LoadAsync(CancellationToken.None);

            Assert.Equal(expectedKey, store.Current.CursorShortcut.Key);
            Assert.Equal(0xff123456u, store.Current.Color);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task LoadAsync_CurrentSettingsWithExplicitH_PreservesChosenCursorBinding()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-migration-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        SettingsStore store = new(NullLogger<SettingsStore>.Instance, path);

        try
        {
            await store.SaveAsync(new AppSettings { CursorShortcut = new ShortcutGesture(0x48, ShortcutModifiers.None) }, CancellationToken.None);
            SettingsStore reloaded = new(NullLogger<SettingsStore>.Instance, path);

            await reloaded.LoadAsync(CancellationToken.None);

            Assert.Equal(0x48, reloaded.Current.CursorShortcut.Key);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task UpdateAsync_ConcurrentIndependentChanges_PreservesEverySetting()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-settings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        SettingsStore store = new(NullLogger<SettingsStore>.Instance, path);

        try
        {
            await Task.WhenAll(store.UpdateAsync(current => current with { Color = 0xff112233 }, CancellationToken.None),
                store.UpdateAsync(current => current with { Thickness = 27 }, CancellationToken.None),
                store.UpdateAsync(current => current with { ShowCursor = false }, CancellationToken.None));
            SettingsStore reloaded = new(NullLogger<SettingsStore>.Instance, path);
            await reloaded.LoadAsync(CancellationToken.None);

            Assert.Equal(0xff112233u, reloaded.Current.Color);
            Assert.Equal(27, reloaded.Current.Thickness);
            Assert.False(reloaded.Current.ShowCursor);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void GetError_CaptureShortcutOverlapsUndo_RejectsConflictingCommands()
    {
        AppSettings settings = new();

        string? error = ShortcutPolicy.GetError(settings with
        {
            CaptureShortcut = settings.UndoShortcut
        });

        Assert.NotNull(error);
    }
}
