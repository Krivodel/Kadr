using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Settings;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class EditorRefinementTests
{
    [Fact]
    public void Defaults_NewSettings_UseDxgiOnDemand()
    {
        AppSettings settings = new();

        Assert.Equal(CaptureBackend.DesktopDuplication, settings.Backend);
        Assert.Equal(CaptureMode.OnDemand, settings.Mode);
    }

    [Fact]
    public void Complete_OutsideClickWithoutMovement_KeepsSelectionAndHistory()
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        PixelRectangle original = new(100, 100, 200, 150);
        document.SetSelection(original);
        DocumentHistory history = new(document);
        EditorInteraction interaction = new(document, history);
        interaction.Begin(new PixelPoint(600, 500), 5);

        interaction.Complete(new PixelPoint(600, 500), true);

        Assert.Equal(original, document.Selection);
        Assert.False(history.CanUndo);
    }

    [Theory]
    [InlineData(50, 170, ResizeHandle.Left)]
    [InlineData(500, 170, ResizeHandle.Right)]
    [InlineData(180, 50, ResizeHandle.Top)]
    [InlineData(180, 500, ResizeHandle.Bottom)]
    [InlineData(50, 50, ResizeHandle.Left | ResizeHandle.Top)]
    [InlineData(500, 50, ResizeHandle.Right | ResizeHandle.Top)]
    [InlineData(50, 500, ResizeHandle.Left | ResizeHandle.Bottom)]
    [InlineData(500, 500, ResizeHandle.Right | ResizeHandle.Bottom)]
    public void GetResizeHandle_OutsideSelection_MatchesActualResizeDirection(double x, double y, ResizeHandle expected)
    {
        using DrawingDocument document = TestDocuments.Create(800, 600);
        document.SetSelection(new PixelRectangle(100, 100, 200, 150));
        EditorInteraction interaction = new(document, new DocumentHistory(document));

        ResizeHandle handle = interaction.GetResizeHandle(new PixelPoint(x, y), 5);

        Assert.Equal(expected, handle);
    }

    [Fact]
    public void Number_ThickDrawingStyle_ScalesWithStrokeAndCanResize()
    {
        DrawingElement element = TestDocuments.Element(DrawingToolId.Number, 100, 100, 100, 100) with
        {
            Thickness = 22
        };

        DrawingElement resized = element.Resize(new PixelRectangle(100, 100, 60, 60));

        Assert.Equal(44, element.Bounds.Width);
        Assert.Equal(60, resized.Bounds.Width, 6);
    }
}
