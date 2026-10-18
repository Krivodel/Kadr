using Kadr.Desktop.Services.Input;
using Kadr.Desktop.Services.Settings;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class PreviewShortcutTests
{
    [Theory]
    [InlineData(0x12, ShortcutModifiers.None, true, false, false, true)]
    [InlineData(0x12, ShortcutModifiers.None, false, false, false, false)]
    [InlineData(0x12, ShortcutModifiers.None, true, true, false, true)]
    [InlineData(0x77, ShortcutModifiers.Control, false, true, true, true)]
    [InlineData(0x77, ShortcutModifiers.Control, false, false, true, false)]
    [InlineData(0x77, ShortcutModifiers.Control, true, true, true, false)]
    [InlineData(0, ShortcutModifiers.None, true, true, true, false)]
    public void IsHeld_KeyAndModifiers_PreserveModifierPreviewAndMatchChords(int key, ShortcutModifiers modifiers,
        bool alt, bool control, bool functionKey, bool expected)
    {
        ShortcutGesture gesture = new(key, modifiers);
        Func<int, bool> isKeyDown = pressed => pressed switch
        {
            0x12 => alt,
            0x11 => control,
            0x77 => functionKey,
            _ => false
        };

        bool held = KeyboardGestures.IsHeld(gesture, isKeyDown);

        Assert.Equal(expected, held);
    }

    [Theory]
    [InlineData(0xa4, 0x12, ShortcutModifiers.Alt)]
    [InlineData(0xa5, 0x12, ShortcutModifiers.Alt)]
    [InlineData(0xa0, 0x10, ShortcutModifiers.Shift)]
    [InlineData(0xa3, 0x11, ShortcutModifiers.Control)]
    public void Recorder_ModifierReleased_RecordsNormalizedPreviewBinding(int key, int normalized, ShortcutModifiers modifier)
    {
        ShortcutGesture? recorded = null;
        ShortcutRecordingSession session = new(gesture => recorded = gesture, () => { }, () => { }, ShortcutRecordingMode.Hold);

        bool suppressDown = session.Press(new ShortcutGesture(key, modifier), TimeSpan.Zero);
        Assert.Null(recorded);
        bool suppressUp = session.Release(key, TimeSpan.FromMilliseconds(100));

        Assert.Equal(new ShortcutGesture(normalized, ShortcutModifiers.None), recorded);
        Assert.False(suppressDown);
        Assert.False(suppressUp);
    }

    [Fact]
    public void Recorder_ModifierThenKey_RecordsChordInsteadOfModifier()
    {
        List<ShortcutGesture> recorded = [];
        ShortcutRecordingSession session = new(recorded.Add, () => { }, () => { }, ShortcutRecordingMode.Hold);
        ShortcutGesture expected = new(0x77, ShortcutModifiers.Control);

        session.Press(new ShortcutGesture(0xa2, ShortcutModifiers.None), TimeSpan.Zero);
        session.Press(expected, TimeSpan.FromMilliseconds(10));
        session.Release(0xa2, TimeSpan.FromMilliseconds(20));
        Assert.Empty(recorded);
        session.Release(expected.Key, TimeSpan.FromMilliseconds(30));

        Assert.Equal(new ShortcutGesture[] { expected }, recorded);
    }

    [Fact]
    public void Recorder_CommandMode_ContinuesIgnoringStandaloneModifiers()
    {
        List<ShortcutGesture> recorded = [];
        ShortcutRecordingSession session = new(recorded.Add, () => { }, () => { });

        session.Press(new ShortcutGesture(0xa4, ShortcutModifiers.None), TimeSpan.Zero);
        session.Release(0xa4, TimeSpan.FromMilliseconds(100));

        Assert.Empty(recorded);
    }
}
