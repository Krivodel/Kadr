namespace Kadr.Desktop.Services.Geometry;

public static class ToolbarPlacement
{
    private static readonly (bool Right, bool Bottom)[] Corners = new (bool, bool)[]
    {
        (true, true), (false, true), (true, false), (false, false)
    };

    public static (PixelRectangle Actions, PixelRectangle Tools) PlacePair(PixelRectangle selection,
        (double Width, double Height) actionsSize, (double Width, double Height) toolsSize,
        PixelRectangle viewport, double gap)
    {
        foreach ((bool right, bool bottom) in Corners)
        {
            double actionsX = right ? selection.Right + gap : selection.X - gap - actionsSize.Width;
            double toolsY = bottom ? selection.Bottom + gap : selection.Y - gap - toolsSize.Height;
            double actionsY = Math.Clamp(bottom ? selection.Bottom - actionsSize.Height : selection.Y,
                viewport.Y, Math.Max(viewport.Y, viewport.Bottom - actionsSize.Height));
            double toolsX = Math.Clamp(right ? selection.Right - toolsSize.Width : selection.X,
                viewport.X, Math.Max(viewport.X, viewport.Right - toolsSize.Width));
            double[] actionsPositions = new double[] { actionsY, toolsY - gap - actionsSize.Height, toolsY + toolsSize.Height + gap };
            double[] toolsPositions = new double[] { toolsX, actionsX - gap - toolsSize.Width, actionsX + actionsSize.Width + gap };

            foreach (double candidateActionsY in actionsPositions)
            {
                foreach (double candidateToolsX in toolsPositions)
                {
                    PixelRectangle actions = new(actionsX, candidateActionsY, actionsSize.Width, actionsSize.Height);
                    PixelRectangle tools = new(candidateToolsX, toolsY, toolsSize.Width, toolsSize.Height);
                    if (Contains(viewport, actions) && Contains(viewport, tools) && actions.Intersect(tools).IsEmpty)
                    {
                        return (actions, tools);
                    }
                }
            }
        }

        foreach ((bool right, bool bottom) in Corners)
        {
            double groupWidth = actionsSize.Width + gap + toolsSize.Width;
            double groupHeight = Math.Max(actionsSize.Height, toolsSize.Height);
            double left = Math.Clamp(right ? selection.Right - groupWidth : selection.X,
                viewport.X, Math.Max(viewport.X, viewport.Right - groupWidth));
            double top = bottom ? selection.Bottom + gap : selection.Y - gap - groupHeight;
            PixelRectangle actions = new(right ? left + groupWidth - actionsSize.Width : left,
                bottom ? top : top + groupHeight - actionsSize.Height, actionsSize.Width, actionsSize.Height);
            PixelRectangle tools = new(right ? left : left + actionsSize.Width + gap,
                bottom ? top : top + groupHeight - toolsSize.Height, toolsSize.Width, toolsSize.Height);

            if (FitsOutside(actions, tools, selection, viewport))
            {
                return (actions, tools);
            }

            groupWidth = Math.Max(actionsSize.Width, toolsSize.Width);
            groupHeight = actionsSize.Height + gap + toolsSize.Height;
            left = right ? selection.Right + gap : selection.X - gap - groupWidth;
            top = Math.Clamp(bottom ? selection.Bottom - groupHeight : selection.Y,
                viewport.Y, Math.Max(viewport.Y, viewport.Bottom - groupHeight));
            actions = new PixelRectangle(right ? left : left + groupWidth - actionsSize.Width,
                bottom ? top : top + toolsSize.Height + gap, actionsSize.Width, actionsSize.Height);
            tools = new PixelRectangle(right ? left : left + groupWidth - toolsSize.Width,
                bottom ? top + actionsSize.Height + gap : top, toolsSize.Width, toolsSize.Height);

            if (FitsOutside(actions, tools, selection, viewport))
            {
                return (actions, tools);
            }
        }

        double actionsWidth = Math.Min(actionsSize.Width, viewport.Width);
        double actionsHeight = Math.Min(actionsSize.Height, viewport.Height);
        double toolsWidth = Math.Max(1, Math.Min(toolsSize.Width, viewport.Width - actionsWidth - gap));
        double toolsHeight = Math.Min(toolsSize.Height, viewport.Height);
        return (new PixelRectangle(viewport.Right - actionsWidth, viewport.Bottom - actionsHeight, actionsWidth, actionsHeight),
            new PixelRectangle(viewport.Right - actionsWidth - gap - toolsWidth, viewport.Bottom - toolsHeight, toolsWidth, toolsHeight));
    }

    private static bool Contains(PixelRectangle outer, PixelRectangle inner)
    {
        return (inner.X >= outer.X) && (inner.Y >= outer.Y) && (inner.Right <= outer.Right) && (inner.Bottom <= outer.Bottom);
    }

    private static bool FitsOutside(PixelRectangle actions, PixelRectangle tools, PixelRectangle selection, PixelRectangle viewport)
    {
        return Contains(viewport, actions) && Contains(viewport, tools) && actions.Intersect(tools).IsEmpty
            && actions.Intersect(selection).IsEmpty && tools.Intersect(selection).IsEmpty;
    }
}
