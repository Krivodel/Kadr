using System.Collections.Immutable;

using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Settings;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class EditorBehaviorTests
{
    [Fact]
    public void WheelSizeTransition_HalfwayAndFinished_ChangesSmoothlyWithoutOvershooting()
    {
        WheelSizeTransition transition = new();
        transition.Begin(10, 11, TimeSpan.Zero);

        double middle = transition.Advance(TimeSpan.FromMilliseconds(30));
        double end = transition.Advance(WheelSizeTransition.Duration + TimeSpan.FromMilliseconds(10));

        Assert.InRange(middle, 10.01, 10.99);
        Assert.Equal(11, end);
        Assert.False(transition.IsActive);
    }

    [Fact]
    public void Settings_NewProfile_UsesRequestedEditorDefaults()
    {
        AppSettings settings = new();

        Assert.True(settings.AutoSelectCreatedElements);
        Assert.True(settings.ExitImmediately);
        Assert.False(settings.HideCursorWhileDrawing);
        Assert.False(settings.ShowCursor);
        Assert.True(settings.AutoStart);
        Assert.Null(settings.LocalizationId);
        Assert.Equal(OutsideSelectionBehavior.Reselect, settings.OutsideSelectionBehavior);
        Assert.Equal(14.143375000000002, settings.Thickness);
        Assert.Equal(7, settings.BlurStrength);
        Assert.Equal(0xffff0000u, settings.Color);
        Assert.Equal(12, DrawingDefaults.GetLabelSize(DrawingDefaults.MinimumThickness));
        Assert.Equal(256, DrawingDefaults.GetLabelSize(DrawingDefaults.MaximumThickness));
    }

    [Theory]
    [InlineData(DrawingToolId.Pencil)]
    [InlineData(DrawingToolId.Marker)]
    public void Draw_ConstrainedStrokeChangesDirection_ReplacesPathFromOrigin(DrawingToolId tool)
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(new PixelRectangle(100, 100, 600, 400));
        EditorInteraction interaction = new(document, new DocumentHistory(document)) { Tool = tool };
        PixelPoint start = new(200, 200);
        interaction.Begin(start, 5);
        interaction.Update(new PixelPoint(500, 205), true);

        interaction.Update(new PixelPoint(205, 500), true);

        DrawingElement element = Assert.Single(document.Elements);
        Assert.Equal(2, element.Points.Count);
        Assert.Equal(start, element.Points[0]);
        Assert.Equal(200, element.Points[1].X, 6);
        Assert.Equal(500, element.Points[1].Y, 6);
    }

    [Fact]
    public void Draw_DefaultCompletion_LeavesToolActiveWithoutObjectSelection()
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(document.Bounds);
        EditorInteraction interaction = new(document, new DocumentHistory(document)) { Tool = DrawingToolId.Rectangle };
        interaction.Begin(new PixelPoint(100, 100), 5);

        interaction.Complete(new PixelPoint(300, 300), false);

        Assert.Single(document.Elements);
        Assert.Null(interaction.SelectedElement);
        Assert.Equal(DrawingToolId.Rectangle, interaction.Tool);
    }

    [Fact]
    public void Begin_LargeElementCoversSelectionEdge_PrioritizesScreenshotResize()
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        DrawingElement element = TestDocuments.Element(DrawingToolId.Blur, 20, 20, 760, 560);
        document.SetElements(ImmutableArray.Create(element));
        document.SetSelection(new PixelRectangle(100, 100, 400, 300));
        EditorInteraction interaction = new(document, new DocumentHistory(document));

        interaction.Begin(new PixelPoint(99, 250), 5);
        interaction.Complete(new PixelPoint(60, 250), false);

        Assert.Equal(60, document.Selection.X);
        Assert.Equal(element, Assert.Single(document.Elements));
    }

    [Theory]
    [InlineData(DrawingToolId.Line)]
    [InlineData(DrawingToolId.Blur)]
    [InlineData(DrawingToolId.Text)]
    [InlineData(DrawingToolId.Number)]
    public void Wheel_SelectedOldElement_ChangesOnlyItAndGroupsUndo(DrawingToolId tool)
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(document.Bounds);
        DrawingElement element = TestDocuments.Element(tool, 100, 200, 300, 300) with { Thickness = 12, Text = "text" };
        document.SetElements(ImmutableArray.Create(element));
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history);
        PixelPoint hit = tool == DrawingToolId.Line ? new PixelPoint(200, 250) : new PixelPoint(110, 210);
        interaction.Begin(hit, 5);
        interaction.Complete(hit, false);
        object group = new();

        interaction.AdjustSize(2, group);
        interaction.AdjustSize(3, group);

        DrawingElement changed = Assert.Single(document.Elements);
        Assert.NotEqual(element, changed);
        Assert.Equal(DrawingDefaults.Thickness, interaction.Thickness);
        Assert.Equal(tool == DrawingToolId.Blur ? changed.BlurStrength : DrawingDefaults.BlurStrength, interaction.BlurStrength);
        if (tool == DrawingToolId.Blur)
        {
            Assert.Equal(element.BlurStrength + 5, changed.BlurStrength);
            Assert.Equal(element.Thickness, changed.Thickness);
        }
        if (tool == DrawingToolId.Text)
        {
            interaction.SetText(element.Text);
        }
        history.Undo();
        Assert.Equal(element, Assert.Single(document.Elements));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Text_CreateAtPointer_CentersFirstLineVertically()
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(document.Bounds);
        EditorInteraction interaction = new(document, new DocumentHistory(document)) { Tool = DrawingToolId.Text, Thickness = 10 };
        PixelPoint pointer = new(200, 200);

        interaction.Begin(pointer, 5);

        DrawingElement element = Assert.Single(document.Elements);
        Assert.Equal(pointer.Y, element.Bounds.Y + DrawingDefaults.GetTextLineHeight(10) / 2, 6);
    }

    [Fact]
    public void Wheel_ClampedFirstStepThenReverse_DoesNotLoseUndoCheckpoint()
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(document.Bounds);
        DrawingElement element = TestDocuments.Element(DrawingToolId.Rectangle, 100, 100, 400, 400) with { Thickness = 1 };
        document.SetElements(ImmutableArray.Create(element));
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history);
        interaction.Begin(new PixelPoint(100, 250), 5);
        interaction.Complete(new PixelPoint(100, 250), false);
        object group = new();
        interaction.AdjustSize(-1, group);

        interaction.AdjustSize(1, group);

        Assert.True(history.CanUndo);
        history.Undo();
        Assert.Equal(element, Assert.Single(document.Elements));
    }

    [Fact]
    public void Begin_ActiveToolAtScreenshotBoundary_ResizesScreenshotBeforeDrawing()
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(new PixelRectangle(100, 100, 400, 300));
        EditorInteraction interaction = new(document, new DocumentHistory(document)) { Tool = DrawingToolId.Arrow };

        interaction.Begin(new PixelPoint(100, 250), 5);
        interaction.Complete(new PixelPoint(60, 250), false);

        Assert.Equal(60, document.Selection.X);
        Assert.Empty(document.Elements);
    }

    [Fact]
    public void Begin_ElementOutsideScreenshotAwayFromBorder_RemainsSelectableAndMovable()
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        PixelRectangle selection = new(100, 100, 300, 300);
        document.SetSelection(selection);
        DrawingElement element = TestDocuments.Element(DrawingToolId.Blur, 500, 200, 600, 300);
        document.SetElements(ImmutableArray.Create(element));
        EditorInteraction interaction = new(document, new DocumentHistory(document));

        interaction.Begin(new PixelPoint(550, 250), 5);
        interaction.Complete(new PixelPoint(570, 270), false);

        Assert.Equal(selection, document.Selection);
        Assert.Equal(520, Assert.Single(document.Elements).Bounds.X);
        Assert.Equal(220, Assert.Single(document.Elements).Bounds.Y);
    }
}
