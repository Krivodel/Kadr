using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using SkiaSharp;

namespace Kadr.Desktop.Services.Rendering;

public sealed class DocumentRenderer : IDisposable
{
    public bool IsDisposed
    {
        get; private set;
    }

    private readonly DrawingDocument _document;
    private readonly DrawingToolRegistry _tools;
    private readonly Dictionary<Guid, (DrawingElement Element, int Number, SKPicture Input, SKPicture Drawing, SKPicture Picture)> _elements = [];
    private readonly Dictionary<SKPicture, DrawingSource> _sources = new(ReferenceEqualityComparer.Instance);
    private SKImage? _background;
    private SKImage? _cursorBackground;
    private SKPicture? _scene;
    private SKPicture? _backgroundScene;
    private bool _backgroundShowsCursor;
    private long _revision = -1;

    public DocumentRenderer(DrawingDocument document, DrawingToolRegistry tools)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    public SKImage CreatePreview()
    {
        using SKPicture scene = CreateScene();
        return Rasterize(scene, _document.Desktop.Image.Info);
    }

    public SKPicture CreateScene()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if ((_scene is null) || (_revision != _document.Revision))
        {
            RebuildScene();
        }

        using SKPictureRecorder recorder = new();
        SKCanvas canvas = recorder.BeginRecording(GetBounds());
        canvas.DrawPicture(_scene ?? throw new InvalidOperationException("The document scene has not been recorded."));
        return recorder.EndRecording();
    }

    public SKBitmap Export()
    {
        ValidateSelection();
        using SKPicture scene = CreateScene();
        return RenderSelection(scene, _document.Selection, _document.Desktop.Image.Info);
    }

    public async Task<SKBitmap> ExportAsync(CancellationToken ct)
    {
        ValidateSelection();
        using SKPicture scene = CreateScene();
        PixelRectangle selection = _document.Selection;
        SKImageInfo info = _document.Desktop.Image.Info;
        return await Task.Run(() => RenderSelection(scene, selection, info), ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        _scene?.Dispose();
        _backgroundScene?.Dispose();
        _background?.Dispose();
        _cursorBackground?.Dispose();

        foreach ((DrawingElement _, int _, SKPicture _, SKPicture drawing, SKPicture picture) in _elements.Values)
        {
            drawing.Dispose();
            picture.Dispose();
        }

        _elements.Clear();
        foreach (DrawingSource source in _sources.Values)
        {
            source.Dispose();
        }
        _sources.Clear();
    }

    private static SKImage Rasterize(SKPicture scene, SKImageInfo info)
    {
        using SKSurface surface = SKSurface.Create(info);
        surface.Canvas.DrawPicture(scene);
        return surface.Snapshot();
    }

    private static SKBitmap RenderSelection(SKPicture scene, PixelRectangle selection, SKImageInfo info)
    {
        using SKImage preview = Rasterize(scene, info);
        SKBitmap result = new((int)selection.Width, (int)selection.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

        try
        {
            using SKCanvas canvas = new(result);
            canvas.DrawImage(preview, -(float)selection.X, -(float)selection.Y);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private SKRect GetBounds()
    {
        return new SKRect(0, 0, _document.Desktop.Image.Width, _document.Desktop.Image.Height);
    }

    private void ValidateSelection()
    {
        if (_document.Selection.IsEmpty)
        {
            throw new InvalidOperationException("An export requires a nonempty selection.");
        }
    }

    private void RebuildScene()
    {
        _background ??= SKImage.FromBitmap(_document.Desktop.Image);
        SKImage background = _background;

        if (_document.ShowCursor && (_document.Desktop.Cursor is CapturedCursor cursor))
        {
            if (_cursorBackground is null)
            {
                using SKBitmap composed = _document.Desktop.Image.Copy();
                CursorCompositor.Draw(composed, cursor, (int)_document.Desktop.Bounds.X, (int)_document.Desktop.Bounds.Y);
                composed.SetImmutable();
                _cursorBackground = SKImage.FromBitmap(composed);
            }

            background = _cursorBackground;
        }

        if ((_backgroundScene is null) || (_backgroundShowsCursor != _document.ShowCursor))
        {
            using SKPictureRecorder backgroundRecorder = new();
            backgroundRecorder.BeginRecording(GetBounds()).DrawImage(background, 0, 0, SKSamplingOptions.Default);
            _backgroundScene?.Dispose();
            _backgroundScene = backgroundRecorder.EndRecording();
            _backgroundShowsCursor = _document.ShowCursor;
        }

        SKPicture prefix = _backgroundScene;
        int number = 0;
        HashSet<Guid> retained = [];
        HashSet<SKPicture> retainedSources = new(ReferenceEqualityComparer.Instance);

        foreach (DrawingElement element in _document.Elements)
        {
            int elementNumber = element.Tool == DrawingToolId.Number ? ++number : 0;
            retained.Add(element.Id);

            DrawingTool tool = _tools.Get(element.Tool);
            retainedSources.Add(prefix);
            if (!_sources.TryGetValue(prefix, out DrawingSource? source))
            {
                source = new DrawingSource(prefix, _document.Desktop.Image.Info);
                _sources.Add(prefix, source);
            }
            bool exists = _elements.TryGetValue(element.Id, out (DrawingElement Element, int Number, SKPicture Input, SKPicture Drawing, SKPicture Picture) cached);
            bool drawingChanged = !exists || !ReferenceEquals(cached.Element, element) || (cached.Number != elementNumber)
                || (tool.UsesSource && !ReferenceEquals(cached.Input, prefix));

            if (drawingChanged || !ReferenceEquals(cached.Input, prefix))
            {
                SKPicture drawing;

                if (drawingChanged)
                {
                    using SKPictureRecorder drawingRecorder = new();
                    SKCanvas drawingCanvas = drawingRecorder.BeginRecording(GetBounds());
                    tool.Draw(drawingCanvas, element, elementNumber, source);
                    drawing = drawingRecorder.EndRecording();
                    cached.Drawing?.Dispose();
                }
                else
                {
                    drawing = cached.Drawing;
                }

                using SKPictureRecorder elementRecorder = new();
                SKCanvas elementCanvas = elementRecorder.BeginRecording(GetBounds());
                elementCanvas.DrawPicture(prefix);
                elementCanvas.DrawPicture(drawing);
                SKPicture picture = elementRecorder.EndRecording();
                cached.Picture?.Dispose();
                cached = (element, elementNumber, prefix, drawing, picture);
                _elements[element.Id] = cached;
            }

            prefix = cached.Picture;
        }

        using SKPictureRecorder recorder = new();
        recorder.BeginRecording(GetBounds()).DrawPicture(prefix);
        _scene?.Dispose();
        _scene = recorder.EndRecording();
        _revision = _document.Revision;

        foreach (SKPicture removedSource in _sources.Keys.Where(key => !retainedSources.Contains(key)).ToArray())
        {
            _sources[removedSource].Dispose();
            _sources.Remove(removedSource);
        }

        foreach (Guid removed in _elements.Keys.Where(id => !retained.Contains(id)).ToArray())
        {
            _elements[removed].Drawing.Dispose();
            _elements[removed].Picture.Dispose();
            _elements.Remove(removed);
        }
    }
}
