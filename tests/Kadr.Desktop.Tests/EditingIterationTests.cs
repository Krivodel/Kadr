using System.Collections.Immutable;
using System.Text.Json;

using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class EditingIterationTests
{
    [Fact]
    public void WheelScale_FastBurst_ReachesMaximumInTwelveStepsAndReversesPrecisely()
    {
        WheelSizeScale scale = new();
        double value = DrawingDefaults.Thickness;
        for (int step = 0; step < 12; step++)
        {
            value = scale.GetAcceleratedTarget(value, 1, 1, 128, TimeSpan.FromMilliseconds(step * 30));
        }

        Assert.Equal(128, value);
        Assert.InRange(WheelSizeScale.GetTarget(value, -0.1, 1, 128), 1.01, 127.99);
        Assert.InRange(WheelSizeScale.GetTarget(1, 0.1, 1, 128), 1.01, 127.99);
    }

    [Fact]
    public void WheelScale_FractionalForwardThenBackward_PreservesSizeWithoutRounding()
    {
        double value = 20;
        for (int step = 0; step < 10; step++)
        {
            value = WheelSizeScale.GetTarget(value, 0.1, 1, 128);
        }

        double restored = WheelSizeScale.GetTarget(value, -1, 1, 128);

        Assert.Equal(21, value, 8);
        Assert.Equal(20, restored, 8);
    }

    [Theory]
    [InlineData(DrawingToolId.Rectangle)]
    [InlineData(DrawingToolId.Ellipse)]
    public void Resize_ExistingShapeConstrained_MatchesSquarePlacementAndSupportsUndo(DrawingToolId tool)
    {
        using DrawingDocument document = TestDocuments.Create(1000, 800);
        document.SetSelection(document.Bounds);
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history) { Tool = tool, AutoSelectCreatedElements = true };
        interaction.Begin(new PixelPoint(100, 100), 5);
        interaction.Complete(new PixelPoint(300, 220), false);
        DrawingElement original = Assert.Single(document.Elements);
        ElementControl corner = ElementEditing.GetControls(original)[3];

        interaction.Begin(corner.Position, 5);
        interaction.Complete(new PixelPoint(400, 300), false, true);

        PixelRectangle resized = Assert.Single(document.Elements).Bounds;
        Assert.Equal(resized.Width, resized.Height, 8);
        Assert.Equal(new PixelPoint(100, 100), new PixelPoint(resized.X, resized.Y));
        history.Undo();
        Assert.Equal(original, Assert.Single(document.Elements));
    }

    [Theory]
    [InlineData(DrawingToolId.Line)]
    [InlineData(DrawingToolId.Arrow)]
    public void Resize_LineEndpointConstrained_SnapsToSameDirectionAsPlacement(DrawingToolId tool)
    {
        DrawingElement element = TestDocuments.Element(tool, 100, 100, 300, 250);
        ElementControl end = ElementEditing.GetControls(element)[1];

        DrawingElement edited = ElementEditing.Edit(element, end, new PixelPoint(450, 120), DrawingConstraint.Strict);

        Assert.Equal(100, edited.Points[^1].Y, 8);
        Assert.Equal(element.Points[0], edited.Points[0]);
        Assert.Equal(120, ElementEditing.Edit(element, end, new PixelPoint(450, 120)).Points[^1].Y);
    }

    [Theory]
    [InlineData(ShortcutModifiers.None, 4)]
    [InlineData(ShortcutModifiers.Control, 4)]
    public async Task Load_SelectionOverride_PreservesBindingAndUsesCurrentDeleteDefault(ShortcutModifiers modifiers, int expectedKey)
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-mouse-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        AppSettings legacy = new()
        {
            AlternativeShortcuts = new Dictionary<EditorAction, ShortcutGesture?> { [EditorAction.Select] = new ShortcutGesture(4, modifiers) }
        };
        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(legacy));
            SettingsStore store = new(NullLogger<SettingsStore>.Instance, path);

            await store.LoadAsync(CancellationToken.None);

            Assert.Equal(new ShortcutGesture(expectedKey, modifiers), ShortcutCatalog.GetAlternative(store.Current, EditorAction.Select));
            Assert.Equal(new ShortcutGesture(2, ShortcutModifiers.None), ShortcutCatalog.Get(store.Current, EditorAction.Delete));
            Assert.Null(ShortcutPolicy.GetError(store.Current));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
