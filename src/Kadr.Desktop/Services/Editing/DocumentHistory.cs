using System.Collections.Immutable;

namespace Kadr.Desktop.Services.Editing;

public sealed class DocumentHistory
{
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public event EventHandler? Changed;

    private const int Capacity = 200;
    private readonly DrawingDocument _document;
    private readonly LinkedList<ImmutableArray<DrawingElement>> _undo = [];
    private readonly Stack<ImmutableArray<DrawingElement>> _redo = new();

    public DocumentHistory(DrawingDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public void Commit(ImmutableArray<DrawingElement> before)
    {
        if (before.SequenceEqual(_document.Elements))
        {
            return;
        }

        _undo.AddLast(before);
        _redo.Clear();

        if (_undo.Count > Capacity)
        {
            _undo.RemoveFirst();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (_undo.Last is LinkedListNode<ImmutableArray<DrawingElement>> previous)
        {
            _redo.Push(_document.Elements);
            _undo.RemoveLast();
            _document.SetElements(previous.Value);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Redo()
    {
        if (_redo.TryPop(out ImmutableArray<DrawingElement> next))
        {
            _undo.AddLast(_document.Elements);
            _document.SetElements(next);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public DocumentHistorySnapshot GetSnapshot()
    {
        return new DocumentHistorySnapshot(_undo.ToImmutableArray(), _redo.Reverse().ToImmutableArray());
    }

    public void Restore(DocumentHistorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _undo.Clear();
        _redo.Clear();
        foreach (ImmutableArray<DrawingElement> elements in snapshot.Undo.TakeLast(Capacity))
        {
            _undo.AddLast(elements);
        }
        foreach (ImmutableArray<DrawingElement> elements in snapshot.Redo.TakeLast(Capacity))
        {
            _redo.Push(elements);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
