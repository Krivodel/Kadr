using System.Collections.Immutable;

namespace Kadr.Desktop.Services.Editing;

public sealed record DrawingSessionSnapshot(ImmutableArray<DrawingElement> Objects, ImmutableArray<int> Current,
    ImmutableArray<ImmutableArray<int>> Undo, ImmutableArray<ImmutableArray<int>> Redo)
{
    public static DrawingSessionSnapshot Capture(DrawingDocument document, DocumentHistory history)
    {
        List<DrawingElement> objects = [];
        Dictionary<DrawingElement, int> indices = new(ReferenceEqualityComparer.Instance);
        DocumentHistorySnapshot frames = history.GetSnapshot();

        int GetIndex(DrawingElement element)
        {
            if (!indices.TryGetValue(element, out int index))
            {
                index = objects.Count;
                objects.Add(element);
                indices.Add(element, index);
            }
            return index;
        }

        ImmutableArray<int> Map(ImmutableArray<DrawingElement> elements)
        {
            return elements.Select(GetIndex).ToImmutableArray();
        }

        ImmutableArray<int> current = Map(document.Elements);
        ImmutableArray<ImmutableArray<int>> undo = frames.Undo.Select(Map).ToImmutableArray();
        ImmutableArray<ImmutableArray<int>> redo = frames.Redo.Select(Map).ToImmutableArray();
        return new DrawingSessionSnapshot(objects.ToImmutableArray(), current, undo, redo);
    }

    public void Restore(DrawingDocument document, DocumentHistory history)
    {
        ImmutableArray<DrawingElement> Resolve(ImmutableArray<int> frame)
        {
            return frame.Select(index => Objects[index]).ToImmutableArray();
        }

        ImmutableArray<DrawingElement> current = Resolve(Current);
        DocumentHistorySnapshot frames = new(Undo.Select(Resolve).ToImmutableArray(), Redo.Select(Resolve).ToImmutableArray());
        document.SetElements(current);
        history.Restore(frames);
    }
}
