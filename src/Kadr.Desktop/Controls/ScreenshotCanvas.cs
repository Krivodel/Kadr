using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Kadr.Desktop.Services.Capture;
using Kadr.Desktop.Services.Editing;
using Kadr.Desktop.Services.Geometry;
using Kadr.Desktop.Services.Rendering;
using Kadr.Desktop.ViewModels;

using PixelPoint = Kadr.Desktop.Services.Geometry.PixelPoint;

namespace Kadr.Desktop.Controls;

public sealed class ScreenshotCanvas : Control
{
    internal Action? FrameRendered { get; set; }

    public double DimAmount
    {
        get => GetValue(DimAmountProperty);
        set => SetValue(DimAmountProperty, value);
    }
    public double PreviewAmount
    {
        get => GetValue(PreviewAmountProperty);
        set => SetValue(PreviewAmountProperty, value);
    }
    public EditorViewModel? Editor
    {
        get; set;
    }
    public PixelPoint? DrawingPointer { get; set; }
    public bool ShowDrawingPointer { get; set; }
    public DesktopMonitor? Monitor
    {
        get; set;
    }
    public static readonly StyledProperty<double> DimAmountProperty = AvaloniaProperty.Register<ScreenshotCanvas, double>(nameof(DimAmount));
    public static readonly StyledProperty<double> PreviewAmountProperty = AvaloniaProperty.Register<ScreenshotCanvas, double>(nameof(PreviewAmount));

    static ScreenshotCanvas()
    {
        AffectsRender<ScreenshotCanvas>(DimAmountProperty, PreviewAmountProperty);
    }

    public ScreenshotCanvas()
    {
        Transitions = new Transitions
        {
            new DoubleTransition { Property = DimAmountProperty, Duration = UiAnimationDuration.PanelFade },
            new DoubleTransition { Property = PreviewAmountProperty, Duration = UiAnimationDuration.PreviewFade }
        };
    }

    public override void Render(DrawingContext context)
    {
        if ((Editor is not EditorViewModel editor) || editor.Renderer.IsDisposed || (Monitor is not DesktopMonitor monitor))
        {
            return;
        }

        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        double originX = monitor.Bounds.X - editor.Document.Desktop.Bounds.X;
        double originY = monitor.Bounds.Y - editor.Document.Desktop.Bounds.Y;
        context.Custom(new ScreenshotDrawOperation(new Rect(Bounds.Size), editor.Renderer.CreateScene(), originX, originY, 1 / scaling, FrameRendered));
        PixelRectangle selection = editor.Document.Selection;
        Rect local = new((selection.X - originX) / scaling, (selection.Y - originY) / scaling,
            selection.Width / scaling, selection.Height / scaling);
        IBrush dim = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(Math.Round((130 + (125 * PreviewAmount)) * DimAmount), 0, 255), 0, 0, 0));
        Rect viewport = new(Bounds.Size);

        if (selection.IsEmpty)
        {
            context.FillRectangle(dim, viewport);
            return;
        }

        StreamGeometry shade = new();
        using (StreamGeometryContext geometry = shade.Open())
        {
            geometry.BeginFigure(viewport.TopLeft, true);
            geometry.LineTo(viewport.TopRight);
            geometry.LineTo(viewport.BottomRight);
            geometry.LineTo(viewport.BottomLeft);
            geometry.EndFigure(true);
            geometry.BeginFigure(local.TopLeft, true);
            geometry.LineTo(local.BottomLeft);
            geometry.LineTo(local.BottomRight);
            geometry.LineTo(local.TopRight);
            geometry.EndFigure(true);
        }

        context.DrawGeometry(dim, null, shade);

        using (context.PushOpacity(Math.Clamp(DimAmount * (1 - PreviewAmount), 0, 1)))
        {
            double pixel = 1 / scaling;
            Rect frame = new((Math.Round(local.Left * scaling) + 0.5) / scaling, (Math.Round(local.Top * scaling) + 0.5) / scaling,
                Math.Max(0, local.Width - pixel), Math.Max(0, local.Height - pixel));
            Pen outline = new(Brushes.White, pixel, new DashStyle(new double[] { 4, 4 }, 0));
            context.DrawLine(outline, frame.TopLeft, frame.TopRight);
            context.DrawLine(outline, frame.BottomLeft, frame.BottomRight);
            context.DrawLine(outline, frame.TopLeft, frame.BottomLeft);
            context.DrawLine(outline, frame.TopRight, frame.BottomRight);

            if (!editor.Interaction.IsDrawing && editor.Interaction.SelectedElement is DrawingElement selected)
            {
                IReadOnlyList<ElementControl> controls = ElementEditing.GetControls(selected);
                if (controls.Any(control => control.Kind == ElementControlKind.Bounds))
                {
                    PixelRectangle rectangle = selected.Bounds;
                    context.DrawRectangle(null, new Pen(Brushes.DeepSkyBlue, 1), new Rect((rectangle.X - originX) / scaling,
                        (rectangle.Y - originY) / scaling, rectangle.Width / scaling, rectangle.Height / scaling));
                }
                foreach (ElementControl control in controls)
                {
                    Point point = new((control.Position.X - originX) / scaling, (control.Position.Y - originY) / scaling);
                    context.DrawEllipse(Brushes.DeepSkyBlue, new Pen(Brushes.Black, 1), point, 3.5, 3.5);
                }
            }
            if (ShowDrawingPointer && DrawingPointer is PixelPoint pointer && editor.Interaction.Tool is DrawingToolId tool && tool != DrawingToolId.Blur)
            {
                Point point = new((pointer.X - originX) / scaling, (pointer.Y - originY) / scaling);
                double strokeThickness = DrawingDefaults.GetStrokeThickness(tool, editor.Interaction.Thickness);
                double extent = (tool is DrawingToolId.Text ? DrawingDefaults.GetTextLineHeight(editor.Interaction.Thickness)
                    : tool is DrawingToolId.Number ? DrawingDefaults.GetNumberSize(editor.Interaction.Thickness)
                    : tool is DrawingToolId.Arrow ? ArrowTool.GetPreviewDiameter(editor.Interaction.Thickness) : strokeThickness) / scaling + 8;
                using DrawingContext.PushedState shadow = editor.Tools.Single(item => item.Id == tool).HasShadow ? context.PushEffect(new DropShadowEffect
                {
                    BlurRadius = DrawingShadow.Sigma * 2 / scaling, OffsetX = 0, OffsetY = DrawingShadow.OffsetY / scaling,
                    Opacity = DrawingShadow.Opacity / 255d, Color = Colors.Black
                }, new Rect(point.X - extent, point.Y - extent, extent * 2, extent * 2)) : default;
                Color color = Color.FromUInt32(editor.Interaction.Color);
                if (tool == DrawingToolId.Marker)
                {
                    color = Color.FromArgb((byte)(color.A * DrawingDefaults.MarkerOpacity / 255), color.R, color.G, color.B);
                }
                IBrush brush = new SolidColorBrush(color);
                if (tool == DrawingToolId.Text)
                {
                    double height = DrawingDefaults.GetTextLineHeight(editor.Interaction.Thickness) / scaling;
                    point = new Point(point.X, point.Y - height / 2);
                    Pen pen = new(brush, Math.Max(1 / scaling, height / 28));
                    context.DrawLine(pen, point, new Point(point.X, point.Y + height));
                    context.DrawLine(pen, new Point(point.X - height / 7, point.Y), new Point(point.X + height / 7, point.Y));
                    context.DrawLine(pen, new Point(point.X - height / 7, point.Y + height), new Point(point.X + height / 7, point.Y + height));
                }
                else if (tool == DrawingToolId.Number)
                {
                    double diameter = DrawingDefaults.GetNumberSize(editor.Interaction.Thickness);
                    double radius = diameter / (2 * scaling);
                    double stroke = Math.Min(editor.Interaction.Thickness, diameter / 8) / scaling;
                    context.DrawEllipse(Brushes.White, new Pen(brush, stroke), point, radius, radius);
                }
                else if (tool != DrawingToolId.Blur)
                {
                    double radius = (tool == DrawingToolId.Arrow ? ArrowTool.GetPreviewDiameter(editor.Interaction.Thickness) : strokeThickness) / (2 * scaling);
                    context.DrawEllipse(brush, null, point, radius, radius);
                }
            }
        }
    }

}
