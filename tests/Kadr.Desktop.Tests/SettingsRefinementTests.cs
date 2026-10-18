using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Input;
using Kadr.Desktop.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class SettingsRefinementTests
{
    [Theory]
    [InlineData(9, "Tab")]
    [InlineData(20, "CapsLock")]
    [InlineData(8, "Backspace")]
    [InlineData(0x71, "F2")]
    [InlineData(0x60, "Num 0")]
    [InlineData(0, "")]
    public void ShortcutDisplay_StandardKey_UsesReadableName(int key, string expected)
    {
        Assert.Equal(expected, new ShortcutGesture(key, ShortcutModifiers.None).DisplayName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(14.143375)]
    [InlineData(100)]
    public void Wheel_IsolatedNotches_ArePreciseAtEverySize(double original)
    {
        WheelSizeScale scale = new();

        double first = scale.GetAcceleratedTarget(original, 1, 1, 128, TimeSpan.Zero);
        double second = scale.GetAcceleratedTarget(first, 1, 1, 128, TimeSpan.FromSeconds(1));

        Assert.Equal(original + 1, first, 8);
        Assert.Equal(original + 2, second, 8);
        Assert.Equal(second - 0.25, scale.GetAcceleratedTarget(second, -0.25, 1, 128, TimeSpan.FromMilliseconds(1010)), 8);
    }

    [Fact]
    public void Wheel_FractionalEvents_EqualWholeDetentsWithoutEventCountAcceleration()
    {
        WheelSizeScale fine = new();
        WheelSizeScale coarse = new();
        double fineValue = 10;
        double coarseValue = 10;
        for (int index = 0; index < 50; index++)
        {
            fineValue = fine.GetAcceleratedTarget(fineValue, 0.1, 1, 128, TimeSpan.FromMilliseconds(index));
        }
        for (int index = 0; index < 5; index++)
        {
            coarseValue = coarse.GetAcceleratedTarget(coarseValue, 1, 1, 128, TimeSpan.FromMilliseconds(index * 10));
        }

        Assert.Equal(coarseValue, fineValue, 8);
        Assert.Equal(39, fineValue, 8);
    }

    [Fact]
    public void Recorder_HeldSpace_ClearsAtHalfSecondAndWaitsForRelease()
    {
        int clears = 0;
        int releases = 0;
        List<ShortcutGesture> recorded = [];
        ShortcutRecordingSession session = new(recorded.Add, () => clears++, () => releases++);
        ShortcutGesture space = new(32, ShortcutModifiers.None);
        session.Press(space, TimeSpan.Zero);
        session.Press(space, TimeSpan.FromMilliseconds(300));
        session.Tick(TimeSpan.FromMilliseconds(499));
        Assert.Equal(0, clears);
        Assert.InRange(session.ClearProgress, 0.99, 1);

        session.Tick(TimeSpan.FromMilliseconds(500));
        session.Tick(TimeSpan.FromMilliseconds(800));
        session.Press(space, TimeSpan.FromMilliseconds(850));
        Assert.Equal(1, clears);
        Assert.Empty(recorded);
        Assert.Equal(0, releases);
        session.Release(32, TimeSpan.FromMilliseconds(900));
        Assert.Equal(1, releases);
        Assert.False(session.IsHoldingSpace);
    }

    [Theory]
    [InlineData(ShortcutModifiers.None, 499)]
    [InlineData(ShortcutModifiers.Control, 1000)]
    public void Recorder_ShortSpaceOrModifiedSpace_RecordsKeyWithoutClearing(ShortcutModifiers modifiers, int duration)
    {
        ShortcutGesture? recorded = null;
        int clears = 0;
        ShortcutRecordingSession session = new(gesture => recorded = gesture, () => clears++, () => { });
        ShortcutGesture expected = new(32, modifiers);

        session.Press(expected, TimeSpan.Zero);
        session.Release(32, TimeSpan.FromMilliseconds(duration));

        Assert.Equal(expected, recorded);
        Assert.Equal(0, clears);
    }

    [Fact]
    public async Task Settings_ClearedPrimaryAndAlternative_PersistWithoutBindingEmptyGesture()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-empty-shortcuts-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        SettingsStore store = new(NullLogger<SettingsStore>.Instance, path);
        AppSettings settings = new()
        {
            CaptureShortcut = ShortcutGesture.Empty,
            EditorShortcuts = new Dictionary<EditorAction, ShortcutGesture> { [EditorAction.Arrow] = ShortcutGesture.Empty },
            AlternativeShortcuts = new Dictionary<EditorAction, ShortcutGesture?> { [EditorAction.Arrow] = null }
        };
        try
        {
            Assert.Null(ShortcutPolicy.GetError(settings));
            await store.SaveAsync(settings, CancellationToken.None);
            SettingsStore restored = new(NullLogger<SettingsStore>.Instance, path);
            await restored.LoadAsync(CancellationToken.None);

            Assert.Empty(ShortcutCatalog.GetBindings(restored.Current, EditorAction.Capture));
            Assert.Empty(ShortcutCatalog.GetBindings(restored.Current, EditorAction.Arrow));
            Assert.True(ShortcutCatalog.Get(restored.Current, EditorAction.Arrow).IsEmpty);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
