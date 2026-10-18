using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class EditorInteractionTests
{
    [Fact]
    public void Complete_DragOutsideExistingSelection_ResizesInsteadOfCreatingAndDoesNotQuickCopy()
    {
        using DrawingDocument document = TestDocuments.Create(400, 300);
        document.SetSelection(new PixelRectangle(100, 100, 100, 100));
        EditorInteraction interaction = new(document, new DocumentHistory(document));
        interaction.Begin(new PixelPoint(300, 250), 5);

        bool copy = interaction.Complete(new PixelPoint(350, 270), true);

        Assert.Equal(new PixelRectangle(100, 100, 250, 170), document.Selection);
        Assert.False(copy);
    }

    [Theory]
    [InlineData(DrawingToolId.Arrow)]
    [InlineData(DrawingToolId.Rectangle)]
    [InlineData(DrawingToolId.Blur)]
    public void Complete_ClickWithoutDragging_DoesNotCreateDegenerateShapeOrHistory(DrawingToolId tool)
    {
        using DrawingDocument document = TestDocuments.Create();
        document.SetSelection(document.Bounds);
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history)
        {
            Tool = tool
        };
        interaction.Begin(new PixelPoint(40, 40), 3);

        interaction.Complete(new PixelPoint(40, 40), false);

        Assert.Empty(document.Elements);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Complete_CrossMonitorSelection_NotifiesViewsAfterGestureFinishes()
    {
        using DrawingDocument document = TestDocuments.Create(3840, 1080);
        EditorInteraction interaction = new(document, new DocumentHistory(document));
        bool? activeWhenNotified = null;
        interaction.Changed += (sender, arguments) => { activeWhenNotified = interaction.IsGestureActive; };
        interaction.Begin(new PixelPoint(1700, 300), 5);

        interaction.Complete(new PixelPoint(2400, 700), false);

        Assert.Equal(false, activeWhenNotified);
        Assert.Equal(new PixelRectangle(1700, 300, 700, 400), document.Selection);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Complete_NewSelection_ChecksControlAtRelease(bool controlAtRelease, bool expectedCopy)
    {
        using DrawingDocument document = TestDocuments.Create();
        EditorInteraction interaction = new(document, new DocumentHistory(document));
        interaction.Begin(new PixelPoint(10, 10), 4);

        bool copy = interaction.Complete(new PixelPoint(90, 80), controlAtRelease);

        Assert.Equal(expectedCopy, copy);
        Assert.Equal(new PixelRectangle(10, 10, 80, 70), document.Selection);
    }

    [Fact]
    public void Complete_MovingSelectionWithControl_DoesNotCopyOrMoveAnnotations()
    {
        using DrawingDocument document = TestDocuments.Create();
        document.SetSelection(new PixelRectangle(10, 10, 80, 70));
        DrawingElement annotation = TestDocuments.Element(DrawingToolId.Rectangle, 12, 12, 20, 20);
        document.SetElements(System.Collections.Immutable.ImmutableArray.Create(annotation));
        EditorInteraction interaction = new(document, new DocumentHistory(document));
        interaction.Begin(new PixelPoint(60, 60), 3);

        bool copy = interaction.Complete(new PixelPoint(70, 65), true);

        Assert.False(copy);
        Assert.Equal(new PixelRectangle(20, 15, 80, 70), document.Selection);
        Assert.Equal(annotation, Assert.Single(document.Elements));
    }

    [Fact]
    public void Undo_FreehandGesture_RemovesEntireStrokeAndRedoRestoresIt()
    {
        using DrawingDocument document = TestDocuments.Create();
        document.SetSelection(document.Bounds);
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history)
        {
            Tool = DrawingToolId.Pencil
        };
        interaction.Begin(new PixelPoint(5, 5), 3);
        interaction.Update(new PixelPoint(20, 15));
        interaction.Update(new PixelPoint(40, 20));
        interaction.Complete(new PixelPoint(60, 25), false);

        history.Undo();
        Assert.Empty(document.Elements);
        history.Redo();

        Assert.Equal(5, Assert.Single(document.Elements).Points.Count);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Complete_EmptyText_DoesNotLeaveInvisibleUndoEntry()
    {
        using DrawingDocument document = TestDocuments.Create();
        document.SetSelection(document.Bounds);
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history)
        {
            Tool = DrawingToolId.Text
        };
        interaction.Begin(new PixelPoint(10, 10), 3);
        interaction.Complete(new PixelPoint(10, 10), false);

        interaction.SetText("");

        Assert.Empty(document.Elements);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Undo_TextCreation_RemovesTextInOneStep()
    {
        using DrawingDocument document = TestDocuments.Create();
        document.SetSelection(document.Bounds);
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history)
        {
            Tool = DrawingToolId.Text
        };
        interaction.Begin(new PixelPoint(10, 10), 3);
        interaction.Complete(new PixelPoint(10, 10), false);
        interaction.SetText("Пример");

        history.Undo();

        Assert.Empty(document.Elements);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Cancel_ResizingElement_RestoresOriginalGeometry()
    {
        using DrawingDocument document = TestDocuments.Create();
        document.SetSelection(document.Bounds);
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history)
        {
            Tool = DrawingToolId.Rectangle
        };
        interaction.Begin(new PixelPoint(10, 10), 3);
        interaction.Complete(new PixelPoint(50, 50), false);
        DrawingElement original = Assert.Single(document.Elements);
        interaction.Tool = null;
        interaction.Begin(new PixelPoint(50, 50), 4);
        interaction.Update(new PixelPoint(90, 80));

        interaction.Cancel();

        Assert.Equal(original, Assert.Single(document.Elements));
    }

    [Fact]
    public void Commit_AfterUndo_ClearsRedo()
    {
        using DrawingDocument document = TestDocuments.Create();
        document.SetSelection(document.Bounds);
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history)
        {
            Tool = DrawingToolId.Line
        };
        interaction.Begin(new PixelPoint(10, 10), 3);
        interaction.Complete(new PixelPoint(50, 50), false);
        history.Undo();

        interaction.Begin(new PixelPoint(20, 20), 3);
        interaction.Complete(new PixelPoint(80, 60), false);

        Assert.False(history.CanRedo);
        Assert.Equal(new PixelPoint(20, 20), Assert.Single(document.Elements).Points[0]);
    }
}
