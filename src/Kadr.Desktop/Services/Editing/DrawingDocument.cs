using System.Collections.Immutable;

using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Geometry;

namespace Kadr.Desktop.Services.Editing;

public sealed class DrawingDocument : IDisposable
{
    public CapturedDesktop Desktop
    {
        get;
    }
    public ImmutableArray<DrawingElement> Elements { get; private set; } = ImmutableArray<DrawingElement>.Empty;
    public PixelRectangle Selection
    {
        get; private set;
    }
    public PixelRectangle Bounds => new(0, 0, Desktop.Bounds.Width, Desktop.Bounds.Height);
    public bool ShowCursor
    {
        get; private set;
    }
    public long Revision
    {
        get; private set;
    }

    public event EventHandler? Changed;

    public DrawingDocument(CapturedDesktop desktop, bool showCursor)
    {
        Desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
        ShowCursor = showCursor;
    }

    public void SetElements(ImmutableArray<DrawingElement> elements)
    {
        if (Elements == elements)
        {
            return;
        }

        Elements = elements;
        Revision++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetSelection(PixelRectangle selection)
    {
        PixelRectangle snapped = selection.IsEmpty ? default : selection.Snap(Bounds);

        if (Selection == snapped)
        {
            return;
        }

        Selection = snapped;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetCursorVisible(bool visible)
    {
        if (Desktop.CanToggleCursor)
        {
            ShowCursor = visible;
            Revision++;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        Desktop.Dispose();
    }
}
