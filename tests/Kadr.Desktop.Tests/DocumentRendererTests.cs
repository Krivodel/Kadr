using System.Collections.Immutable;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Rendering;
using SkiaSharp;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class DocumentRendererTests
{
    [Fact]
    public void CreateScene_EditingFirstObject_ReusesDrawingOfLaterIndependentObjects()
    {
        using DrawingDocument document = TestDocuments.Create();
        DrawingElement first = TestDocuments.Element(DrawingToolId.Number, 20, 20, 20, 20);
        DrawingElement second = TestDocuments.Element(DrawingToolId.Number, 100, 80, 100, 80);
        document.SetElements(ImmutableArray.Create(first, second));
        RecordingNumberTool tool = new();
        using DocumentRenderer renderer = new(document, new DrawingToolRegistry(new DrawingTool[] { tool }));
        using SKPicture before = renderer.CreateScene();
        tool.Numbers.Clear();
        document.SetElements(ImmutableArray.Create(first.Translate(10, 10), second));

        using SKPicture after = renderer.CreateScene();

        Assert.Equal(new int[] { 1 }, tool.Numbers);
    }

    [Fact]
    public void CreateScene_EditingOneObject_ReusesUnchangedObjectsAndKeepsPreviousSnapshotAlive()
    {
        using DrawingDocument document = TestDocuments.Create(3840, 2160);
        DrawingElement first = TestDocuments.Element(DrawingToolId.Number, 20, 20, 20, 20);
        DrawingElement second = TestDocuments.Element(DrawingToolId.Number, 100, 100, 100, 100);
        document.SetElements(ImmutableArray.Create(first, second));
        RecordingNumberTool tool = new();
        using DocumentRenderer renderer = new(document, new DrawingToolRegistry(new DrawingTool[] { tool }));
        using SKPicture before = renderer.CreateScene();
        tool.Numbers.Clear();
        document.SetElements(ImmutableArray.Create(first, second.Translate(10, 20)));

        using SKPicture after = renderer.CreateScene();
        renderer.Dispose();
        using SKBitmap pixels = new(100, 100);
        using SKCanvas canvas = new(pixels);
        canvas.DrawPicture(before);
        canvas.DrawPicture(after);

        Assert.Equal(new int[] { 2 }, tool.Numbers);
        Assert.Equal(SKColors.White, pixels.GetPixel(0, 0));
    }

    [Fact]
    public void CreateScene_SelectionChanges_DoNotRerecordAnnotations()
    {
        using DrawingDocument document = TestDocuments.Create(3840, 2160);
        document.SetElements(ImmutableArray.Create(TestDocuments.Element(DrawingToolId.Number, 100, 100, 100, 100)));
        RecordingNumberTool tool = new();
        using DocumentRenderer renderer = new(document, new DrawingToolRegistry(new DrawingTool[] { tool }));
        using SKPicture first = renderer.CreateScene();
        tool.Numbers.Clear();

        for (int index = 0; index < 100; index++)
        {
            document.SetSelection(new PixelRectangle(index, index, 300, 200));
            using SKPicture snapshot = renderer.CreateScene();
        }

        Assert.Empty(tool.Numbers);
    }

    [Fact]
    public void Export_SelectionIntersectingAnnotation_EqualsSamePixelsInPreview()
    {
        using DrawingDocument document = TestDocuments.Create();
        document.SetSelection(new PixelRectangle(30, 30, 50, 40));
        document.SetElements(ImmutableArray.Create(TestDocuments.Element(DrawingToolId.Ellipse, 20, 15, 70, 60)));
        using DocumentRenderer renderer = new(document, new DrawingToolRegistry());
        using SKImage preview = renderer.CreatePreview();
        using SKBitmap previewPixels = SKBitmap.FromImage(preview);

        using SKBitmap export = renderer.Export();

        Assert.Equal(50, export.Width);
        Assert.Equal(40, export.Height);

        for (int y = 0; y < export.Height; y++)
        {
            for (int x = 0; x < export.Width; x++)
            {
                Assert.Equal(previewPixels.GetPixel(x + 30, y + 30), export.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void Export_BlurAfterDrawing_BlursPreviousElementAndLeavesSourceUnchanged()
    {
        using DrawingDocument document = TestDocuments.Create();
        document.SetSelection(document.Bounds);
        DrawingElement line = TestDocuments.Element(DrawingToolId.Line, 20, 40, 120, 40) with
        {
            Thickness = 3
        };
        DrawingElement blur = TestDocuments.Element(DrawingToolId.Blur, 10, 10, 140, 80) with
        {
            Thickness = 6
        };
        document.SetElements(ImmutableArray.Create(line, blur));
        using DocumentRenderer renderer = new(document, new DrawingToolRegistry());

        using SKBitmap export = renderer.Export();

        Assert.True(export.GetPixel(60, 40).Green > 100);
        Assert.Equal(SKColors.White, document.Desktop.Image.GetPixel(60, 40));
    }

    [Fact]
    public void Delete_NumberMiddle_RendersRenumberedSequenceAndUndoRestoresOriginal()
    {
        using DrawingDocument document = TestDocuments.Create(240, 100);
        document.SetSelection(document.Bounds);
        DrawingElement first = TestDocuments.Element(DrawingToolId.Number, 20, 20, 20, 20);
        DrawingElement second = TestDocuments.Element(DrawingToolId.Number, 80, 20, 80, 20);
        DrawingElement third = TestDocuments.Element(DrawingToolId.Number, 140, 20, 140, 20);
        document.SetElements(ImmutableArray.Create(first, second, third));
        DocumentHistory history = new(document);
        RecordingNumberTool recording = new();
        using DocumentRenderer renderer = new(document, new DrawingToolRegistry(new DrawingTool[] { recording }));
        ImmutableArray<DrawingElement> before = document.Elements;
        document.SetElements(before.Remove(second));
        history.Commit(before);
        using SKBitmap removed = renderer.Export();

        Assert.Equal(new int[] { 1, 2 }, recording.Numbers);
        recording.Numbers.Clear();
        history.Undo();
        using SKBitmap restored = renderer.Export();
        Assert.Equal(new int[] { 2, 3 }, recording.Numbers);
    }
}
