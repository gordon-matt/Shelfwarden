namespace Shelfwarden.Components.Shared;

/// <summary>
/// Tracks an in-progress drag over a vertically ordered list and works out the resulting order.
/// Shared by the reading list and universe timeline editors, which both hand the server a
/// complete ordering rather than a single move, so a concurrent edit can't shear the list.
/// </summary>
public sealed class ListReorderState
{
    /// <summary>Row the drag started on, or <c>null</c> when no drag is in progress.</summary>
    public int? SourceIndex { get; private set; }

    /// <summary>Row the pointer is currently over.</summary>
    public int? TargetIndex { get; private set; }

    public bool IsDragging => SourceIndex.HasValue;

    public void Start(int index)
    {
        SourceIndex = index;
        TargetIndex = index;
    }

    public void DragOver(int index)
    {
        if (SourceIndex.HasValue)
        {
            TargetIndex = index;
        }
    }

    public void Cancel()
    {
        SourceIndex = null;
        TargetIndex = null;
    }

    /// <summary>True while <paramref name="index"/> is the row being dragged.</summary>
    public bool IsSource(int index) => SourceIndex == index;

    /// <summary>True while <paramref name="index"/> is the slot the dragged row would land in.</summary>
    public bool IsDropTarget(int index) => IsDragging && TargetIndex == index && SourceIndex != index;

    /// <summary>
    /// Ends the drag and returns <paramref name="ids"/> in their new order, or <c>null</c> when
    /// the drag was a no-op.
    /// </summary>
    public List<T>? Complete<T>(IReadOnlyList<T> ids)
    {
        int? from = SourceIndex;
        int? to = TargetIndex;
        Cancel();

        if (from is not int source || to is not int target || source == target)
        {
            return null;
        }

        return Move(ids, source, target);
    }

    /// <summary>
    /// Returns <paramref name="ids"/> with the item at <paramref name="from"/> lifted out and
    /// re-inserted at <paramref name="to"/>, or <c>null</c> when the move would be a no-op.
    /// </summary>
    public static List<T>? Move<T>(IReadOnlyList<T> ids, int from, int to)
    {
        if (from < 0 || from >= ids.Count || to < 0 || to >= ids.Count || from == to)
        {
            return null;
        }

        var reordered = ids.ToList();
        var moved = reordered[from];
        reordered.RemoveAt(from);
        reordered.Insert(to, moved);
        return reordered;
    }
}
