namespace Shelfwarden.Components.Pages;

public partial class BookCard : ComponentBase
{
    private bool isUpdatingProgress;

    private BookListItemDto? lastSeenBook;

    /// <summary>
    /// Local override applied after a Mark as Read/Unread click so the card reflects the new
    /// state immediately without forcing the parent to re-fetch. Resets when the parent passes
    /// in a new <see cref="Book"/> instance.
    /// </summary>
    private double? progressOverride;

    [Parameter, EditorRequired]
    public BookListItemDto? Book { get; set; }

    /// <summary>
    /// Optional <see cref="RenderFragment"/> appended to the bottom of the context menu (after
    /// a divider). Used by parent screens like <c>CollectionDetail</c> to inject context-specific
    /// actions (e.g. "Remove from collection") without having to fork the card markup. Items
    /// should be `<li>` elements containing `dropdown-item` controls; remember `@onclick:stopPropagation`
    /// so the click doesn't bubble to the outer card anchor and trigger detail-page navigation.
    /// </summary>
    [Parameter]
    public RenderFragment? ExtraMenuItems { get; set; }

    /// <summary>Whether this card is currently part of the parent's selection set.</summary>
    [Parameter]
    public bool IsSelected { get; set; }

    /// <summary>
    /// Raised after a successful Mark as Read / Unread so the parent grid can update its
    /// in-memory <see cref="BookListItemDto"/> (records are immutable). Optional — when not wired
    /// up, the card still reflects the new state via its own override.
    /// </summary>
    [Parameter]
    public EventCallback<BookListItemDto> OnBookChanged { get; set; }

    /// <summary>
    /// Raised when <see cref="SelectionMode"/> is on and the user clicks the card body. The
    /// payload is the desired new selection state (i.e. the inverse of <see cref="IsSelected"/>).
    /// </summary>
    [Parameter]
    public EventCallback<bool> OnSelectionToggled { get; set; }

    /// <summary>
    /// When true, clicking anywhere on the card toggles selection via <see cref="OnSelectionToggled"/>
    /// instead of navigating to the book detail page. Parents typically set this to
    /// <c>selectedBookIds.Count &gt; 0</c> so the card flips into multi-select mode the moment
    /// the user checks any box, and reverts to plain-navigate behaviour once the selection is
    /// cleared. The href stays on the anchor so middle-click / Ctrl-click still works when
    /// selection mode is off.
    /// </summary>
    [Parameter]
    public bool SelectionMode { get; set; }

    private double CurrentProgress => progressOverride ?? Book?.ProgressPercentage ?? 0;

    private bool IsFinished => CurrentProgress >= Constants.FinishedThresholdPercent;

    protected override void OnParametersSet()
    {
        // Drop our local override whenever the parent hands us a new DTO instance — that's our
        // signal that the source of truth has been refreshed (e.g. after a search) and our
        // in-memory tweak is no longer needed.
        if (!ReferenceEquals(Book, lastSeenBook))
        {
            progressOverride = null;
            lastSeenBook = Book;
        }
    }

    private Task HandleCardClickAsync() =>
        // When SelectionMode is off, `@onclick:preventDefault` is false and the anchor's
        // href takes over — nothing to do here. When SelectionMode is on, preventDefault is
        // active so the browser won't navigate; we just flip the selection.
        !SelectionMode || Book is null || !OnSelectionToggled.HasDelegate
            ? Task.CompletedTask
            : OnSelectionToggled.InvokeAsync(!IsSelected);

    private Task NotifyBookChangedAsync(double newPercentage)
    {
        if (Book is null || !OnBookChanged.HasDelegate)
        {
            return Task.CompletedTask;
        }

        var updated = Book with { ProgressPercentage = newPercentage };
        return OnBookChanged.InvokeAsync(updated);
    }

    private async Task ToggleReadAsync()
    {
        if (Book is null || isUpdatingProgress)
        {
            return;
        }

        isUpdatingProgress = true;
        try
        {
            if (IsFinished)
            {
                var result = await BookService.MarkAsUnreadAsync(Book.Id);
                if (result.IsSuccess)
                {
                    progressOverride = 0;
                    await NotifyBookChangedAsync(0);
                }
            }
            else
            {
                var result = await BookService.MarkAsReadAsync(Book.Id);
                if (result.IsSuccess)
                {
                    progressOverride = result.Value.Percentage;
                    await NotifyBookChangedAsync(result.Value.Percentage);
                }
            }
        }
        finally
        {
            isUpdatingProgress = false;
        }
    }
}