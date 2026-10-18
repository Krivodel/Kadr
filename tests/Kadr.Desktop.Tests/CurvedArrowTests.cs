using System.Collections.Immutable;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Rendering;
using SkiaSharp;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class CurvedArrowTests
{
    [Fact]
    public void Edit_MiddleHandle_BendsArrowAndSupportsUndoRedo()
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(document.Bounds);
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history) { Tool = DrawingToolId.Arrow, AutoSelectCreatedElements = true };
        interaction.Begin(new PixelPoint(100, 300), 5);
        interaction.Complete(new PixelPoint(600, 300), false);
        DrawingElement original = Assert.Single(document.Elements);
        ElementControl middle = ElementEditing.GetControls(original).Single(control => control.Kind == ElementControlKind.Bend);

        interaction.Begin(middle.Position, 5);
        interaction.Complete(new PixelPoint(350, 150), false);

        DrawingElement bent = Assert.Single(document.Elements);
        ArrowTool tool = new();
        Assert.True(tool.HitTest(bent, new PixelPoint(350, 150), 1));
        Assert.False(tool.HitTest(bent, new PixelPoint(350, 300), 1));
        Assert.Equal(new PixelPoint(370, 180), bent.Translate(20, 30).Bend);
        history.Undo();
        Assert.Equal(original, Assert.Single(document.Elements));
        history.Redo();
        Assert.Equal(bent, Assert.Single(document.Elements));
    }

    [Theory]
    [InlineData(20, 180, 280, 180, 150, 55, 3)]
    [InlineData(280, 180, 20, 180, 150, 55, 30)]
    [InlineData(100, 100, 101, 101, 100, 100, 128)]
    [InlineData(30, 180, 270, 180, 150, 280, 12)]
    public void Export_CurvedArrow_MatchesPreviewInsideSelection(double x, double y, double endX, double endY, double bendX, double bendY, double thickness)
    {
        using DrawingDocument document = TestDocuments.Create(300, 300);
        document.SetSelection(new PixelRectangle(20, 40, 260, 200));
        document.SetElements(ImmutableArray.Create(TestDocuments.Element(DrawingToolId.Arrow, x, y, endX, endY) with
        {
            Thickness = thickness,
            Bend = new PixelPoint(bendX, bendY)
        }));
        using DocumentRenderer renderer = new(document, new DrawingToolRegistry());
        using SKImage preview = renderer.CreatePreview();
        using SKBitmap previewPixels = SKBitmap.FromImage(preview);

        using SKBitmap export = renderer.Export();

        for (int row = 0; row < export.Height; row++)
        {
            for (int column = 0; column < export.Width; column++)
            {
                Assert.Equal(previewPixels.GetPixel(column + 20, row + 40), export.GetPixel(column, row));
            }
        }
    }
}
