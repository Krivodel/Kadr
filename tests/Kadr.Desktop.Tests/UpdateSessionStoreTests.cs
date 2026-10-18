using System.Collections.Immutable;
using System.Text;

using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Updates;
using Kadr.Desktop.ViewModels;
using SkiaSharp;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class UpdateSessionStoreTests
{
    [Fact]
    public void Capture_LargeUndoHistory_PreservesObjectSharingWithoutRepeatingStrokeData()
    {
        using EditorViewModel editor = EditorViewModelTests.Create(new EditorTestServices());
        for (int index = 0; index < 100; index++)
        {
            ImmutableArray<DrawingElement> before = editor.Document.Elements;
            DrawingElement stroke = new(Guid.NewGuid(), DrawingToolId.Pencil,
                Enumerable.Range(0, 100).Select(point => new PixelPoint(point, index)).ToImmutableList(), 0xffff0000, 3);
            editor.Document.SetElements(before.Add(stroke));
            editor.History.Commit(before);
        }

        string json = System.Text.Json.JsonSerializer.Serialize(EditorSessionSnapshot.Capture(editor));
        EditorSessionSnapshot snapshot = System.Text.Json.JsonSerializer.Deserialize<EditorSessionSnapshot>(json)!;
        using EditorViewModel restored = EditorViewModelTests.Create(new EditorTestServices(), new DrawingDocument(snapshot.CreateDesktop(), false));
        snapshot.Restore(restored);

        Assert.True(json.Length < 500_000, "Repeated history frames must reference stroke data rather than expand it.");
        Assert.Equal(100, restored.Document.Elements.Length);
        Assert.Same(restored.Document.Elements[0], restored.History.GetSnapshot().Undo[^1][0]);
    }

    [Fact]
    public void Capture_UnfinishedGesture_RejectsInconsistentHistory()
    {
        using EditorViewModel editor = EditorViewModelTests.Create(new EditorTestServices());
        editor.Interaction.Tool = DrawingToolId.Arrow;
        editor.Interaction.Begin(new PixelPoint(20, 20), 5);
        editor.Interaction.Update(new PixelPoint(100, 60), false);

        Assert.Throws<InvalidOperationException>(() => EditorSessionSnapshot.Capture(editor));
    }

    [Fact]
    public async Task SaveAndRestore_CompleteSession_RestoresPixelsCursorSelectionObjectsAndHistory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-session-" + Guid.NewGuid().ToString("N"));
        UpdateSessionStore store = new(directory);
        using DrawingDocument source = TestDocuments.Create(320, 240, SKColors.LightGray);
        SKBitmap image = source.Desktop.Image.Copy();
        CapturedCursor cursor = new(CursorShapeKind.Color, new PixelPoint(30, 35), 2, 2, 8, Enumerable.Repeat((byte)255, 16).ToArray(), 0);
        CapturedDesktop desktop = new(image, new DesktopMonitor[] { new(1, new PixelRectangle(-320, 0, 320, 240), "Left") },
            new PixelRectangle(-320, 0, 320, 240), cursor, true, source.Desktop.Metrics);
        using EditorViewModel original = EditorViewModelTests.Create(new EditorTestServices(), new DrawingDocument(desktop, true));
        original.Document.SetSelection(new PixelRectangle(10, 20, 270, 180));
        original.Interaction.Color = 0xff12aaff;
        original.Interaction.Thickness = 17;
        original.Interaction.BlurStrength = 23;
        original.Interaction.Tool = DrawingToolId.Arrow;
        ImmutableArray<DrawingElement> empty = original.Document.Elements;
        DrawingElement arrow = TestDocuments.Element(DrawingToolId.Arrow, 30, 40, 200, 150) with { Bend = new PixelPoint(150, 50) };
        original.Document.SetElements(ImmutableArray.Create(arrow));
        original.History.Commit(empty);
        ImmutableArray<DrawingElement> first = original.Document.Elements;
        original.Document.SetElements(first.Add(TestDocuments.Element(DrawingToolId.Blur, 100, 100, 250, 220)));
        original.History.Commit(first);
        original.History.Undo();
        original.Interaction.RestoreSelection(arrow.Id);
        using SKBitmap expected = original.Renderer.Export();

        try
        {
            string id = await store.SaveAsync(EditorSessionSnapshot.Capture(original), CancellationToken.None);
            byte[] persisted = await File.ReadAllBytesAsync(Path.Combine(directory, id + ".session"));
            Assert.DoesNotContain("SchemaVersion", Encoding.UTF8.GetString(persisted));
            Assert.Equal(id, await store.GetPendingAsync(CancellationToken.None));
            EditorSessionSnapshot snapshot = await store.LoadAsync(id, CancellationToken.None);
            using EditorViewModel restored = EditorViewModelTests.Create(new EditorTestServices(), new DrawingDocument(snapshot.CreateDesktop(), false));
            snapshot.Restore(restored);
            using SKBitmap actual = restored.Renderer.Export();

            Assert.Equal(expected.Bytes, actual.Bytes);
            Assert.Equal(original.Document.Selection, restored.Document.Selection);
            Assert.Equal(original.Document.Desktop.Bounds, restored.Document.Desktop.Bounds);
            Assert.Equal(cursor.Pixels, restored.Document.Desktop.Cursor!.Pixels);
            Assert.True(restored.Document.ShowCursor);
            Assert.Equal(arrow.Id, restored.Interaction.SelectedId);
            Assert.Equal(DrawingToolId.Arrow, restored.Interaction.Tool);
            Assert.Equal(17, restored.Interaction.Thickness);
            Assert.Equal(23, restored.Interaction.BlurStrength);
            restored.History.Redo();
            Assert.Equal(2, restored.Document.Elements.Length);
            restored.History.Undo();
            restored.History.Undo();
            Assert.Empty(restored.Document.Elements);
            store.Complete(id);
            Assert.Null(await store.GetPendingAsync(CancellationToken.None));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Load_TamperedSession_KeepsRecoveryFileAndRejectsContent()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Kadr-session-" + Guid.NewGuid().ToString("N"));
        UpdateSessionStore store = new(directory);
        using EditorViewModel editor = EditorViewModelTests.Create(new EditorTestServices());
        try
        {
            string id = await store.SaveAsync(EditorSessionSnapshot.Capture(editor), CancellationToken.None);
            string path = Path.Combine(directory, id + ".session");
            byte[] bytes = await File.ReadAllBytesAsync(path);
            bytes[^1] ^= 1;
            await File.WriteAllBytesAsync(path, bytes);

            await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() => store.LoadAsync(id, CancellationToken.None));

            Assert.True(File.Exists(path));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync("../elsewhere", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
