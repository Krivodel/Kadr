using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Export;
using Kadr.Desktop.Services.Rendering;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class EditorViewModelTests
{
    [Theory]
    [InlineData(DrawingToolId.Line)]
    [InlineData(DrawingToolId.Arrow)]
    [InlineData(null)]
    public void SelectTool_AfterDrawing_DeselectsObjectWithoutChangingItOrAddingUndo(DrawingToolId? requestedTool)
    {
        using EditorViewModel model = Create(new EditorTestServices());
        model.Document.SetSelection(model.Document.Bounds);
        model.Interaction.AutoSelectCreatedElements = true;
        model.Interaction.Tool = DrawingToolId.Arrow;
        model.Interaction.Begin(new Services.Geometry.PixelPoint(100, 100), 5);
        model.Interaction.Update(new Services.Geometry.PixelPoint(300, 200), false);
        model.Interaction.Complete(new Services.Geometry.PixelPoint(300, 200), false);
        DrawingElement arrow = Assert.Single(model.Document.Elements);
        Assert.Equal(arrow.Id, model.Interaction.SelectedId);

        if (requestedTool is DrawingToolId tool)
        {
            model.SelectToolCommand.Execute(model.Tools.Single(item => item.Id == tool));
        }
        else
        {
            model.SetActiveTool(null);
        }

        Assert.Null(model.Interaction.SelectedId);
        Assert.Equal(requestedTool == DrawingToolId.Line ? DrawingToolId.Line : null, model.Interaction.Tool);
        Assert.Equal(arrow, Assert.Single(model.Document.Elements));
        model.History.Undo();
        Assert.Empty(model.Document.Elements);
    }

    [Fact]
    public void SelectTool_DuringTextEditing_CommitsTextBeforeClearingSelection()
    {
        using EditorViewModel model = Create(new EditorTestServices());
        model.Document.SetSelection(model.Document.Bounds);
        model.Interaction.Tool = DrawingToolId.Text;
        model.Interaction.Begin(new Services.Geometry.PixelPoint(100, 100), 5);
        model.Interaction.Complete(new Services.Geometry.PixelPoint(100, 100), false);
        model.Interaction.UpdateText("Сохранённый текст");
        Assert.True(model.Interaction.IsTextEditRequested);

        model.SelectToolCommand.Execute(model.Tools.Single(tool => tool.Id == DrawingToolId.Arrow));

        Assert.Null(model.Interaction.SelectedId);
        Assert.False(model.Interaction.IsTextEditRequested);
        Assert.Equal("Сохранённый текст", Assert.Single(model.Document.Elements).Text);
        model.History.Undo();
        Assert.Empty(model.Document.Elements);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RememberPreferencesAsync_SettingsChangedDuringSession_PreservesLatestExplicitChoice(bool toolbarChangedLast)
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-preferences-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        SettingsStore settings = new(NullLogger<SettingsStore>.Instance, path);
        using EditorViewModel model = Create(new EditorTestServices(), settings: settings);

        try
        {
            await settings.UpdateAsync(current => current with { Color = 0xff112233, Thickness = 18 }, CancellationToken.None);

            if (toolbarChangedLast)
            {
                model.SetDrawingColor(0xffaabbcc);
                model.SetDrawingThickness(12);
            }

            await model.RememberPreferencesAsync(CancellationToken.None);

            Assert.Equal(toolbarChangedLast ? 0xffaabbccu : 0xff112233u, settings.Current.Color);
            Assert.Equal(toolbarChangedLast ? 12 : 18, settings.Current.Thickness);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task CopyAsync_ClipboardFailure_KeepsEditorAndDocumentOpen()
    {
        EditorTestServices services = new()
        {
            FailClipboard = true
        };
        using EditorViewModel model = Create(services);
        bool closed = false;
        model.CloseRequested += (sender, arguments) => { closed = true; };

        await model.CopyCommand.ExecuteAsync(null);

        Assert.False(closed);
        Assert.False(model.IsLoading);
        Assert.False(string.IsNullOrWhiteSpace(model.ErrorMessage));
        Assert.False(model.Document.Selection.IsEmpty);
    }

    [Fact]
    public async Task CopyAsync_Success_ClosesAfterClipboardWrite()
    {
        EditorTestServices services = new();
        using EditorViewModel model = Create(services);
        int writesWhenClosed = -1;
        model.CloseRequested += (sender, arguments) => { writesWhenClosed = services.ClipboardWrites; };

        await model.CopyCommand.ExecuteAsync(null);

        Assert.Equal(1, writesWhenClosed);
        Assert.False(model.IsLoading);
    }

    [Fact]
    public async Task RecognizeAsync_EmptyText_PreservesClipboardAndEditor()
    {
        EditorTestServices services = new()
        {
            OcrText = "   "
        };
        using EditorViewModel model = Create(services);
        bool closed = false;
        model.CloseRequested += (sender, arguments) => { closed = true; };

        await model.RecognizeCommand.ExecuteAsync(null);

        Assert.Equal(0, services.ClipboardWrites);
        Assert.False(closed);
        Assert.NotNull(model.ErrorMessage);
    }

    [Fact]
    public async Task SaveAsync_CancelledDialog_PreservesEditorWithoutError()
    {
        using EditorViewModel model = Create(new EditorTestServices());
        bool closed = false;
        model.CloseRequested += (sender, arguments) => { closed = true; };

        await model.SaveCommand.ExecuteAsync(null);

        Assert.False(closed);
        Assert.Null(model.ErrorMessage);
    }

    internal static EditorViewModel Create(EditorTestServices services, DrawingDocument? document = null, SettingsStore? settings = null)
    {
        document ??= TestDocuments.Create();
        document.SetSelection(document.Bounds);
        settings ??= UiTestSession.CreateSettingsStore();
        return new EditorViewModel(document, new DrawingToolRegistry(), services, services, new ImageFileWriter(),
            services, settings, NullLogger<EditorViewModel>.Instance);
    }
}
