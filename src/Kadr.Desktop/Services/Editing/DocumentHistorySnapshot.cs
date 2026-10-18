using System.Collections.Immutable;

namespace Kadr.Desktop.Services.Editing;

public sealed record DocumentHistorySnapshot(ImmutableArray<ImmutableArray<DrawingElement>> Undo,
    ImmutableArray<ImmutableArray<DrawingElement>> Redo);
