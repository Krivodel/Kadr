using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.ViewModels;
using SkiaSharp;

namespace Kadr.Desktop.Services.Updates;

public sealed record EditorSessionSnapshot(int SchemaVersion, byte[] Image, SessionMonitor[] Monitors,
    PixelRectangle Bounds, CapturedCursor? Cursor, bool CanToggleCursor, bool ShowCursor,
    PixelRectangle Selection, DrawingSessionSnapshot Drawing,
    DrawingToolId? Tool, Guid? SelectedId, uint Color, double Thickness, double BlurStrength)
{
    public static EditorSessionSnapshot Capture(EditorViewModel editor)
    {
        if (editor.Interaction.IsTextEditRequested || editor.Interaction.IsGestureActive)
        {
            throw new InvalidOperationException("Finish text editing and pointer gestures before taking a session snapshot.");
        }
        CapturedDesktop desktop = editor.Document.Desktop;
        using SKData image = desktop.Image.Encode(SKEncodedImageFormat.Png, 100);
        return new EditorSessionSnapshot(1, image.ToArray(),
            desktop.Monitors.Select(monitor => new SessionMonitor(monitor.Bounds, monitor.Name, monitor.IsHdr)).ToArray(),
            desktop.Bounds, desktop.Cursor, desktop.CanToggleCursor, editor.Document.ShowCursor,
            editor.Document.Selection, DrawingSessionSnapshot.Capture(editor.Document, editor.History),
            editor.Interaction.Tool, editor.Interaction.SelectedId, editor.Interaction.Color,
            editor.Interaction.Thickness, editor.Interaction.BlurStrength);
    }

    public CapturedDesktop CreateDesktop()
    {
        if (SchemaVersion != 1 || Bounds.Width < 1 || Bounds.Height < 1 || Monitors.Length == 0)
        {
            throw new InvalidDataException("Unsupported editor session.");
        }
        SKBitmap image = SKBitmap.Decode(Image) ?? throw new InvalidDataException("Invalid session image.");
        if (image.Width != Bounds.Width || image.Height != Bounds.Height)
        {
            image.Dispose();
            throw new InvalidDataException("Session dimensions do not match.");
        }
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        return new CapturedDesktop(image, Monitors.Select((monitor, index) =>
                new DesktopMonitor(index + 1, monitor.Bounds, monitor.Name, monitor.IsHdr)).ToArray(),
            Bounds, Cursor, CanToggleCursor, new CaptureMetrics(now, now, now, Array.Empty<long>()));
    }

    public void Restore(EditorViewModel editor)
    {
        Drawing.Restore(editor.Document, editor.History);
        editor.Document.SetSelection(Selection);
        editor.Document.SetCursorVisible(ShowCursor);
        editor.Interaction.Tool = Tool;
        editor.Interaction.Color = Color;
        editor.Interaction.Thickness = Thickness;
        editor.Interaction.BlurStrength = BlurStrength;
        editor.Interaction.RestoreSelection(SelectedId);
    }
}
