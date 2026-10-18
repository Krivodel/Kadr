using System.Collections.Immutable;

using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Rendering;
using SkiaSharp;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class ElementEditingTests
{
    [Theory]
    [InlineData(DrawingToolId.Arrow, 3)]
    [InlineData(DrawingToolId.Line, 3)]
    [InlineData(DrawingToolId.Rectangle, 8)]
    [InlineData(DrawingToolId.Ellipse, 8)]
    [InlineData(DrawingToolId.Pencil, 0)]
    [InlineData(DrawingToolId.Marker, 0)]
    [InlineData(DrawingToolId.Text, 0)]
    [InlineData(DrawingToolId.Number, 8)]
    [InlineData(DrawingToolId.Blur, 8)]
    public void Controls_Tool_ReturnsSpecificHandles(DrawingToolId tool, int count)
    {
        DrawingElement element = TestDocuments.Element(tool, 30, 30, 120, 60);

        IReadOnlyList<ElementControl> controls = ElementEditing.GetControls(element);

        Assert.Equal(count, controls.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Resize_NumberEveryHandleAcrossAnchor_MirrorsAndGrowsLikeRectangle(int index)
    {
        using DrawingDocument document = TestDocuments.Create();
        DrawingElement element = TestDocuments.Element(DrawingToolId.Number, 60, 40, 60, 40) with { Thickness = 128, LabelSize = 40 };
        ElementControl control = ElementEditing.GetControls(element)[index];
        PixelRectangle bounds = element.Bounds;
        PixelPoint across = new(bounds.X + bounds.Width / 2 + (bounds.X + bounds.Width / 2 - control.Position.X) * 4,
            bounds.Y + bounds.Height / 2 + (bounds.Y + bounds.Height / 2 - control.Position.Y) * 4);

        DrawingElement resized = ElementEditing.Edit(element, control, across);
        document.SetElements(ImmutableArray.Create(resized));
        document.SetSelection(document.Bounds);
        using DocumentRenderer renderer = new(document, new DrawingToolRegistry());
        using SKBitmap image = renderer.Export();

        Assert.Equal(60, resized.Bounds.Width);
        Assert.Equal(60, resized.Bounds.Height);
        PixelRectangle rectangle = SelectionGeometry.Resize(bounds, control.Edges, across);
        if ((control.Edges & (ResizeHandle.Left | ResizeHandle.Right)) != 0)
        {
            Assert.Equal(rectangle.X, resized.Bounds.X);
        }
        if ((control.Edges & (ResizeHandle.Top | ResizeHandle.Bottom)) != 0)
        {
            Assert.Equal(rectangle.Y, resized.Bounds.Y);
        }
        Assert.True(double.IsFinite(resized.Bounds.X));
        Assert.Equal(160, image.Width);
    }

    [Fact]
    public void Resize_NumberCorner_AnchorsOppositeCornerAndSupportsUndo()
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(document.Bounds);
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history) { Tool = DrawingToolId.Number, AutoSelectCreatedElements = true };
        interaction.Begin(new PixelPoint(200, 200), 5);
        interaction.Complete(new PixelPoint(200, 200), false);
        DrawingElement original = Assert.Single(document.Elements);
        ElementControl control = ElementEditing.GetControls(original)[3];

        interaction.Begin(control.Position, 5);
        interaction.Complete(new PixelPoint(original.Bounds.X + 100, original.Bounds.Y + 100), false);

        DrawingElement resized = Assert.Single(document.Elements);
        Assert.Equal(original.Bounds.X, resized.Bounds.X);
        Assert.Equal(original.Bounds.Y, resized.Bounds.Y);
        Assert.Equal(100, resized.Bounds.Width);
        history.Undo();
        Assert.Equal(original, Assert.Single(document.Elements));
    }

    [Fact]
    public void Bend_LineMidpoint_MatchesCurveHitTestAndMovesTogether()
    {
        DrawingElement original = TestDocuments.Element(DrawingToolId.Line, 20, 100, 140, 100);
        ElementControl control = ElementEditing.GetControls(original)[2];

        DrawingElement bent = ElementEditing.Edit(original, control, new PixelPoint(80, 30));

        Assert.True(new LineTool().HitTest(bent, new PixelPoint(80, 30), 1));
        Assert.False(new LineTool().HitTest(bent, new PixelPoint(80, 100), 1));
        Assert.Equal(new PixelPoint(90, 50), bent.Translate(10, 20).Bend);
    }

    [Theory]
    [InlineData(140, 106, 140, 100)]
    [InlineData(105, 140, 100, 140)]
    [InlineData(138, 132, 135, 135)]
    public void Draw_Shift_ConstrainsEndpoint(double x, double y, double expectedX, double expectedY)
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(document.Bounds);
        EditorInteraction interaction = new(document, new DocumentHistory(document)) { Tool = DrawingToolId.Arrow };
        interaction.Begin(new PixelPoint(100, 100), 5);

        interaction.Complete(new PixelPoint(x, y), false, true);

        DrawingElement element = Assert.Single(document.Elements);
        Assert.Equal(expectedX, element.Points[^1].X, 6);
        Assert.Equal(expectedY, element.Points[^1].Y, 6);
    }

    [Fact]
    public void Wheel_BlurWhileDrawing_IsIndependentAndCommitsAsSingleGesture()
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(document.Bounds);
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history) { Tool = DrawingToolId.Blur, Thickness = 20, BlurStrength = 30 };
        interaction.Begin(new PixelPoint(100, 100), 5);

        interaction.AdjustSize(4);
        interaction.Complete(new PixelPoint(200, 200), false);

        DrawingElement element = Assert.Single(document.Elements);
        Assert.Equal(34, element.BlurStrength);
        Assert.Equal(20, interaction.Thickness);
        history.Undo();
        Assert.Empty(document.Elements);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Text_LiveChanges_UndoRestoresWholeEditingSession()
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(document.Bounds);
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history) { Tool = DrawingToolId.Text };
        interaction.Begin(new PixelPoint(100, 100), 5);
        interaction.Complete(new PixelPoint(100, 100), false);
        interaction.UpdateText("one");
        interaction.UpdateText("one two");

        interaction.SetText("one two");

        Assert.Equal("one two", Assert.Single(document.Elements).Text);
        history.Undo();
        Assert.Empty(document.Elements);
        history.Redo();
        Assert.Equal("one two", Assert.Single(document.Elements).Text);
    }

    [Fact]
    public void DeleteAt_RightClickOnTopmost_DeletesOnlyHitAndCanUndo()
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        DrawingElement bottom = TestDocuments.Element(DrawingToolId.Rectangle, 100, 100, 200, 200);
        DrawingElement top = TestDocuments.Element(DrawingToolId.Number, 140, 140, 140, 140);
        document.SetElements(ImmutableArray.Create(bottom, top));
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history) { Tool = DrawingToolId.Arrow };

        interaction.DeleteAt(new PixelPoint(150, 150), 1);

        Assert.Equal(bottom, Assert.Single(document.Elements));
        Assert.Equal(DrawingToolId.Arrow, interaction.Tool);
        history.Undo();
        Assert.Equal(2, document.Elements.Length);
    }
}
