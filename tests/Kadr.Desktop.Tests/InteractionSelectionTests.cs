using System.Collections.Immutable;

using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class InteractionSelectionTests
{
    [Theory]
    [InlineData(DrawingToolId.Arrow)]
    [InlineData(DrawingToolId.Text)]
    [InlineData(DrawingToolId.Number)]
    public void Begin_SelectedFrameWithDrawingTool_MovesInsteadOfDrawingAndPreservesUndo(DrawingToolId activeTool)
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(document.Bounds);
        DrawingElement element = TestDocuments.Element(DrawingToolId.Ellipse, 200, 200, 400, 400);
        document.SetElements(ImmutableArray.Create(element));
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history);
        interaction.Begin(new PixelPoint(200, 300), 5);
        interaction.Complete(new PixelPoint(200, 300), false);
        interaction.Tool = activeTool;
        PixelPoint border = new(250, 200);
        Assert.True(interaction.CanMoveElementAt(border, 5));

        interaction.Begin(border, 5);
        interaction.Complete(new PixelPoint(290, 230), false);

        DrawingElement moved = Assert.Single(document.Elements);
        Assert.Equal(element.Translate(40, 30).Bounds, moved.Bounds);
        Assert.Equal(activeTool, interaction.Tool);
        history.Undo();
        Assert.Equal(element, Assert.Single(document.Elements));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Drag_TextReturnsToOrigin_DoesNotOpenTextEditorOrCreateUndoEntry()
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(document.Bounds);
        DrawingElement text = TestDocuments.Element(DrawingToolId.Text, 100, 200, 100, 200) with { Text = "Text" };
        document.SetElements(ImmutableArray.Create(text));
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history) { Tool = DrawingToolId.Text };
        interaction.Begin(new PixelPoint(110, 210), 5);
        interaction.Update(new PixelPoint(160, 210));

        interaction.Complete(new PixelPoint(110, 210), false);

        Assert.False(interaction.IsTextEditRequested);
        Assert.Equal(text, Assert.Single(document.Elements));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public async Task Blur_AdjustExisting_SetsNextBlurDefaultAndPersistsIt()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-blur-default-" + Guid.NewGuid().ToString("N"));
        SettingsStore store = new(NullLogger<SettingsStore>.Instance, Path.Combine(directory, "settings.json"));
        using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(800, 600), store);
        model.Document.SetSelection(model.Document.Bounds);
        DrawingElement blur = TestDocuments.Element(DrawingToolId.Blur, 100, 100, 200, 200);
        model.Document.SetElements(ImmutableArray.Create(blur));
        model.Interaction.Tool = DrawingToolId.Blur;
        model.Interaction.Begin(new PixelPoint(150, 150), 5);
        model.Interaction.Complete(new PixelPoint(150, 150), false);
        try
        {
            model.AdjustDrawingSize(7);
            model.Interaction.Begin(new PixelPoint(400, 300), 5);
            model.Interaction.Complete(new PixelPoint(500, 400), false);
            await model.RememberPreferencesAsync(CancellationToken.None);

            Assert.All(model.Document.Elements, element => Assert.Equal(blur.BlurStrength + 7, element.BlurStrength));
            Assert.Equal(blur.BlurStrength + 7, store.Current.BlurStrength);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Theory]
    [InlineData(OutsideSelectionBehavior.Resize)]
    [InlineData(OutsideSelectionBehavior.Reselect)]
    public void Drag_OutsideSelection_UsesConfiguredBehavior(OutsideSelectionBehavior behavior)
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(new PixelRectangle(100, 100, 200, 150));
        EditorInteraction interaction = new(document, new DocumentHistory(document)) { OutsideSelectionBehavior = behavior };

        interaction.Begin(new PixelPoint(20, 20), 5);
        interaction.Complete(new PixelPoint(60, 80), false);

        Assert.Equal(behavior == OutsideSelectionBehavior.Resize ? new PixelRectangle(60, 80, 240, 170)
            : new PixelRectangle(20, 20, 40, 60), document.Selection);
    }

    [Theory]
    [InlineData(DrawingToolId.Arrow)]
    [InlineData(DrawingToolId.Pencil)]
    [InlineData(DrawingToolId.Blur)]
    public void Click_ActiveDrawingTool_SelectsExistingWithoutHistoryEntry(DrawingToolId tool)
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(document.Bounds);
        DrawingElement line = TestDocuments.Element(DrawingToolId.Line, 100, 200, 300, 200);
        document.SetElements(ImmutableArray.Create(line));
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history) { Tool = tool };

        interaction.Begin(new PixelPoint(200, 200), 5);
        interaction.Complete(new PixelPoint(201, 200), false);

        Assert.Equal(line, Assert.Single(document.Elements));
        Assert.Equal(line.Id, interaction.SelectedId);
        Assert.False(history.CanUndo);
        Assert.Equal(tool, interaction.Tool);
    }

    [Fact]
    public void Drag_ActiveToolOverElement_CreatesNewElementAndCancelsAtomically()
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(document.Bounds);
        DrawingElement line = TestDocuments.Element(DrawingToolId.Line, 100, 200, 300, 200);
        document.SetElements(ImmutableArray.Create(line));
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history) { Tool = DrawingToolId.Rectangle };
        interaction.Begin(new PixelPoint(200, 200), 5);
        interaction.Update(new PixelPoint(400, 400));
        Assert.Equal(2, document.Elements.Length);

        interaction.Cancel();

        Assert.Equal(line, Assert.Single(document.Elements));
        Assert.False(history.CanUndo);
    }

    [Theory]
    [InlineData(DrawingToolId.Text)]
    [InlineData(DrawingToolId.Number)]
    public void Click_LabelTool_SelectsOnlySameTypeAndCreatesOnOtherElements(DrawingToolId tool)
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(document.Bounds);
        DrawingElement line = TestDocuments.Element(DrawingToolId.Line, 100, 200, 300, 200);
        DrawingElement label = TestDocuments.Element(tool, 350, 200, 350, 200) with { Text = "Label", LabelSize = 50 };
        document.SetElements(ImmutableArray.Create(line, label));
        EditorInteraction interaction = new(document, new DocumentHistory(document)) { Tool = tool };
        interaction.Begin(new PixelPoint(360, 210), 5);
        interaction.Complete(new PixelPoint(360, 210), false);
        Assert.Equal(label.Id, interaction.SelectedId);
        if (tool == DrawingToolId.Text)
        {
            interaction.SetText(label.Text);
        }

        interaction.Begin(new PixelPoint(200, 200), 5);
        interaction.Complete(new PixelPoint(200, 200), false);

        Assert.Equal(3, document.Elements.Length);
        Assert.Equal(tool, document.Elements[^1].Tool);
    }

    [Theory]
    [InlineData(DrawingToolId.Rectangle)]
    [InlineData(DrawingToolId.Ellipse)]
    public void Click_LineInsideLaterOutline_SelectsLineBeforeShapeInterior(DrawingToolId shape)
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(document.Bounds);
        DrawingElement line = TestDocuments.Element(DrawingToolId.Line, 180, 250, 320, 250);
        DrawingElement outline = TestDocuments.Element(shape, 100, 100, 400, 400);
        document.SetElements(ImmutableArray.Create(line, outline));
        EditorInteraction interaction = new(document, new DocumentHistory(document)) { Tool = DrawingToolId.Arrow };

        interaction.Begin(new PixelPoint(250, 250), 5);
        interaction.Complete(new PixelPoint(250, 250), false);

        Assert.Equal(line.Id, interaction.SelectedId);
    }

    [Fact]
    public void ApplySettings_OpenDocument_UpdatesEditorFlagsAndPreservesAnnotationsAndTool()
    {
        using EditorViewModel model = EditorViewModelTests.Create(new EditorTestServices(), TestDocuments.Create(800, 600));
        model.Document.SetSelection(new PixelRectangle(100, 100, 400, 300));
        DrawingElement line = TestDocuments.Element(DrawingToolId.Line, 200, 200, 300, 200);
        model.Document.SetElements(ImmutableArray.Create(line));
        model.Interaction.Tool = DrawingToolId.Arrow;
        model.Interaction.Color = 0xffff0000;

        model.ApplySettings(new AppSettings { AutoSelectCreatedElements = true, OutsideSelectionBehavior = OutsideSelectionBehavior.Reselect });

        Assert.True(model.Interaction.AutoSelectCreatedElements);
        Assert.Equal(OutsideSelectionBehavior.Reselect, model.Interaction.OutsideSelectionBehavior);
        Assert.Equal(line, Assert.Single(model.Document.Elements));
        Assert.Equal(DrawingToolId.Arrow, model.Interaction.Tool);
        Assert.Equal(0xffff0000u, model.Interaction.Color);
        Assert.Equal(new PixelRectangle(100, 100, 400, 300), model.Document.Selection);
    }
}
