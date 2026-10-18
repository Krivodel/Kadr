using System.Collections.Immutable;
using Kadr.Desktop.Services.Geometry;

namespace Kadr.Desktop.Services.Editing;

public static class ElementEditing
{
    public static IReadOnlyList<ElementControl> GetControls(DrawingElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.Tool is DrawingToolId.Pencil or DrawingToolId.Marker or DrawingToolId.Text)
        {
            return Array.Empty<ElementControl>();
        }

        if (element.Tool is DrawingToolId.Arrow or DrawingToolId.Line)
        {
            List<ElementControl> controls =
            [
                new(ElementControlKind.Start, element.Points[0]), new(ElementControlKind.End, element.Points[^1])
            ];
            controls.Add(new ElementControl(ElementControlKind.Bend, element.Bend ?? Midpoint(element.Points[0], element.Points[^1])));
            return controls;
        }

        PixelRectangle bounds = element.Bounds;
        return new ElementControl[]
        {
            new(ElementControlKind.Bounds, new PixelPoint(bounds.X, bounds.Y), ResizeHandle.Left | ResizeHandle.Top),
            new(ElementControlKind.Bounds, new PixelPoint(bounds.Right, bounds.Y), ResizeHandle.Right | ResizeHandle.Top),
            new(ElementControlKind.Bounds, new PixelPoint(bounds.X, bounds.Bottom), ResizeHandle.Left | ResizeHandle.Bottom),
            new(ElementControlKind.Bounds, new PixelPoint(bounds.Right, bounds.Bottom), ResizeHandle.Right | ResizeHandle.Bottom),
            new(ElementControlKind.Bounds, new PixelPoint(bounds.X + bounds.Width / 2, bounds.Y), ResizeHandle.Top),
            new(ElementControlKind.Bounds, new PixelPoint(bounds.X + bounds.Width / 2, bounds.Bottom), ResizeHandle.Bottom),
            new(ElementControlKind.Bounds, new PixelPoint(bounds.X, bounds.Y + bounds.Height / 2), ResizeHandle.Left),
            new(ElementControlKind.Bounds, new PixelPoint(bounds.Right, bounds.Y + bounds.Height / 2), ResizeHandle.Right)
        };
    }

    public static ElementControl? HitControl(DrawingElement element, PixelPoint position, double radius)
    {
        return GetControls(element).Where(control => control.Position.DistanceTo(position) <= radius)
            .Select(control => (ElementControl?)control).MinBy(control => control?.Position.DistanceTo(position) ?? double.MaxValue);
    }

    public static DrawingElement Edit(DrawingElement element, ElementControl control, PixelPoint position, DrawingConstraint constraint = DrawingConstraint.Free)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (constraint == DrawingConstraint.Strict && control.Kind is ElementControlKind.Start or ElementControlKind.End)
        {
            PixelPoint anchor = control.Kind == ElementControlKind.Start ? element.Points[^1] : element.Points[0];
            position = DrawingConstraints.SnapDirection(anchor, position);
        }
        if (control.Kind == ElementControlKind.Start)
        {
            return element with { Points = element.Points.SetItem(0, position) };
        }
        if (control.Kind == ElementControlKind.End)
        {
            return element with { Points = element.Points.SetItem(element.Points.Count - 1, position) };
        }
        if (control.Kind == ElementControlKind.Bend)
        {
            return element with { Bend = position };
        }

        PixelRectangle target = element.Tool == DrawingToolId.Number || constraint == DrawingConstraint.Strict
            ? ResizeSquare(element.Bounds, control.Edges, position)
            : SelectionGeometry.Resize(element.Bounds, control.Edges, position);
        if (element.Tool != DrawingToolId.Number)
        {
            return element.Resize(target);
        }
        PixelPoint origin = new(target.X, target.Y);
        return element with { Points = ImmutableList.Create(origin, origin), LabelSize = target.Width };
    }

    public static bool HitFrame(DrawingElement element, PixelPoint position, double radius)
    {
        ArgumentNullException.ThrowIfNull(element);
        IReadOnlyList<ElementControl> controls = GetControls(element);
        return controls.Any(control => control.Kind == ElementControlKind.Bounds)
            && SelectionGeometry.HitHandle(element.Bounds, position, radius) != ResizeHandle.None
            && !controls.Any(control => control.Position.DistanceTo(position) <= radius);
    }

    public static PixelPoint Midpoint(PixelPoint start, PixelPoint end)
    {
        return new PixelPoint((start.X + end.X) / 2, (start.Y + end.Y) / 2);
    }

    private static PixelRectangle ResizeSquare(PixelRectangle bounds, ResizeHandle edges, PixelPoint position)
    {
        bool horizontal = (edges & (ResizeHandle.Left | ResizeHandle.Right)) != 0;
        bool vertical = (edges & (ResizeHandle.Top | ResizeHandle.Bottom)) != 0;
        double anchorX = edges.HasFlag(ResizeHandle.Left) ? bounds.Right : bounds.X;
        double anchorY = edges.HasFlag(ResizeHandle.Top) ? bounds.Bottom : bounds.Y;
        if (horizontal && vertical)
        {
            position = DrawingConstraints.SnapSquare(new PixelPoint(anchorX, anchorY), position);
        }
        double width = Math.Abs(position.X - anchorX);
        double height = Math.Abs(position.Y - anchorY);
        double size = Math.Max(1, horizontal ? width : height);
        double left = horizontal ? position.X < anchorX ? anchorX - size : anchorX : bounds.X + (bounds.Width - size) / 2;
        double top = vertical ? position.Y < anchorY ? anchorY - size : anchorY : bounds.Y + (bounds.Height - size) / 2;
        return new PixelRectangle(left, top, size, size);
    }


}
