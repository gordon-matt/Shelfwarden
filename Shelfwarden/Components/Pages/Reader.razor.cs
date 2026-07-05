using Microsoft.AspNetCore.Components.Web;

namespace Shelfwarden.Components.Pages;

public partial class Reader : ComponentBase
{
    private readonly List<BookmarkDto> bookmarks = [];
    private BookDto? book;
    private string? bookmarkError;
    private bool jsMounted;
    private int? expandedBookmarkId;
    private string bookmarkNoteDraft = string.Empty;
    private bool bookmarkNoteSaving;
    private string? bookmarkNoteFlash;

    /// <summary>Tracks which <see cref="Id"/> we last loaded — client navigation between <c>/read/1</c> and <c>/read/2</c> reuses this component.</summary>
    private int lastHandledBookId = -1;

    private DateTime? lastSavedAt;
    private string? lastSavedCfi;
    private string? loadError;
    private int pdfCurrentPage = 1;
    private int pdfPageCount;
    private double? progressPercent;
    private DotNetObjectReference<Reader>? selfRef;
    private bool showBookmarks;
    [Parameter] public int Id { get; set; }

    public async ValueTask DisposeAsync() => await TearDownReaderInteropAsync();

    /// <summary>
    /// Called from JS via <see cref="DotNetObjectReference"/> whenever the rendered location
    /// changes (debounced on the JS side). We persist progress immediately so the user can
    /// resume from any device.
    /// </summary>
    [JSInvokable]
    public async Task OnProgress(double percentage, int? page, string? cfi)
    {
        // Avoid Blazor re-renders when epub.js emits duplicate relocated events (same CFI/%).
        // Full rerenders were visibly "flickering" the reader and could race the JS rendition chain.
        bool uiNeedsUpdate =
            !progressPercent.HasValue
            || Math.Abs(progressPercent.Value - percentage) >= 0.5
            || !string.Equals(lastSavedCfi, cfi, StringComparison.Ordinal)
            || (page.HasValue && pdfCurrentPage != page.Value);

        progressPercent = percentage;
        lastSavedCfi = cfi;
        if (page.HasValue)
        {
            pdfCurrentPage = page.Value;
        }

        var result = await BookService.SaveProgressAsync(Id, new SaveProgressRequest
        {
            Percentage = Math.Clamp(percentage, 0, 100),
            PageNumber = page,
            Location = cfi,
        });
        if (result.IsSuccess)
        {
            lastSavedAt = result.Value.LastReadAt;
        }

        if (uiNeedsUpdate)
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (book is null || jsMounted)
        {
            return;
        }

        // Nothing to mount for unsupported formats — mark complete so we don't re-enter forever.
        if (book.FileFormat is not EbookFormat.Epub and not EbookFormat.Pdf)
        {
            jsMounted = true;
            return;
        }

        string fileUrl = $"/files/{Id}";

        try
        {
            if (book.FileFormat == EbookFormat.Epub)
            {
                await MountEpubAsync(fileUrl);
            }
            else
            {
                await MountPdfAsync(fileUrl);
            }

            jsMounted = true;
        }
        catch (Exception ex) when (IsBenignJsInteropFailure(ex))
        {
            // Circuit dropped, user navigated away, or interop timed out — don't surface to the global error UI.
            selfRef?.Dispose();
            selfRef = null;
        }
        catch (Exception)
        {
            selfRef?.Dispose();
            selfRef = null;
            loadError = "Could not initialize the reader for this book.";
            jsMounted = true;
            await InvokeAsync(StateHasChanged);
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (lastHandledBookId == Id)
        {
            return;
        }

        if (lastHandledBookId >= 0)
        {
            await TearDownReaderInteropAsync();
        }

        ResetTransientReaderState();

        var result = await BookService.GetByIdAsync(Id);
        if (!result.IsSuccess)
        {
            loadError = result.Status switch
            {
                ResultStatus.NotFound => "Book not found",
                ResultStatus.Unauthorized => "You need to sign in to read books",
                _ => "Failed to load book",
            };
            book = null;
            lastHandledBookId = Id;
            return;
        }

        book = result.Value;
        loadError = null;
        lastHandledBookId = Id;
        await LoadBookmarksAsync();
    }

    private static string DefaultBookmarkTitle(BookmarkDto bm)
        => bm.PageNumber.HasValue ? $"Page {bm.PageNumber}" : "Saved spot";

    private static string FormatRelative(DateTime utc)
    {
        var delta = DateTime.UtcNow - utc;
        return delta < TimeSpan.FromSeconds(5)
            ? "just now"
            : delta < TimeSpan.FromMinutes(1)
            ? $"{(int)delta.TotalSeconds}s ago"
            : delta < TimeSpan.FromHours(1) ? $"{(int)delta.TotalMinutes}m ago" : utc.ToLocalTime().ToString("HH:mm");
    }

    /// <summary>JS interop failed because the connection ended or the call was canceled — not an app bug.</summary>
    private static bool IsBenignJsInteropFailure(Exception ex)
        => ex is JSDisconnectedException or OperationCanceledException;

    private async Task AddBookmarkAsync()
    {
        if (book is null)
        {
            return;
        }

        bookmarkError = null;

        // EPUB: ask the renderer for the current spot. Fall back to the last CFI we saved
        // via OnProgress in case the JS side hasn't been initialised yet.
        // PDF: we don't get progress callbacks, so the bookmark uses the page count when
        // marked-as-read or the cover (page 1) when nothing is known.
        int? pageNumber = null;
        string? cfi = null;
        if (book.FileFormat == EbookFormat.Epub)
        {
            try
            {
                var location = await JS.InvokeAsync<EpubLocation?>("shelfwardenReader.getEpubLocation");
                cfi = location?.Cfi ?? lastSavedCfi;
            }
            catch (JSDisconnectedException) { }

            if (string.IsNullOrEmpty(cfi))
            {
                bookmarkError = "Open the book to a page before bookmarking.";
                return;
            }
        }
        else if (book.FileFormat == EbookFormat.Pdf)
        {
            // pdf.js tracks the visible page via IntersectionObserver, so we can ask the
            // viewer directly instead of forcing the user to type a number.
            try
            {
                pageNumber = await JS.InvokeAsync<int>("shelfwardenReader.getPdfPage");
            }
            catch (JSDisconnectedException) { }

            if (!pageNumber.HasValue || pageNumber.Value <= 0)
            {
                pageNumber = pdfCurrentPage > 0 ? pdfCurrentPage : 1;
            }
        }

        var request = new CreateBookmarkRequest
        {
            Title = $"Page @ {DateTime.Now:HH:mm}",
            PageNumber = pageNumber,
            Location = cfi,
        };
        var result = await BookmarkService.CreateAsync(Id, request);
        if (result.IsSuccess)
        {
            bookmarks.Add(result.Value);
            showBookmarks = true;
        }
        else
        {
            bookmarkError = result.Errors.FirstOrDefault() ?? "Could not save bookmark.";
        }
        await InvokeAsync(StateHasChanged);
    }

    private async Task DeleteBookmarkAsync(BookmarkDto bm)
    {
        var result = await BookmarkService.DeleteAsync(bm.Id);
        if (result.IsSuccess)
        {
            bookmarks.Remove(bm);
        }
    }

    private async Task GotoBookmarkAsync(BookmarkDto bm)
    {
        if (book is null)
        {
            return;
        }

        if (book.FileFormat == EbookFormat.Epub && !string.IsNullOrEmpty(bm.Location))
        {
            await InvokeReaderAsync("gotoEpub", bm.Location);
        }
        else if (book.FileFormat == EbookFormat.Pdf && bm.PageNumber.HasValue)
        {
            await InvokeReaderAsync("gotoPdfPage", bm.PageNumber.Value);
        }

        showBookmarks = false;
    }

    private void ToggleBookmarkNotes(BookmarkDto bm)
    {
        bookmarkNoteFlash = null;
        if (expandedBookmarkId == bm.Id)
        {
            expandedBookmarkId = null;
            return;
        }

        expandedBookmarkId = bm.Id;
        bookmarkNoteDraft = bm.Note ?? string.Empty;
    }

    private async Task SaveBookmarkNoteAsync(BookmarkDto bm)
    {
        bookmarkNoteSaving = true;
        bookmarkNoteFlash = null;
        try
        {
            var result = await BookmarkService.UpdateAsync(bm.Id, new UpdateBookmarkRequest
            {
                Title = bm.Title,
                Note = bookmarkNoteDraft,
            });

            if (result.IsSuccess)
            {
                int index = bookmarks.FindIndex(b => b.Id == bm.Id);
                if (index >= 0)
                {
                    bookmarks[index] = result.Value;
                }
                bookmarkNoteFlash = "Saved.";
            }
        }
        finally
        {
            bookmarkNoteSaving = false;
        }
    }

    private Task GoToFirstPageAsync() =>
        book?.FileFormat == EbookFormat.Pdf
            ? InvokeReaderAsync("gotoPdfPage", 1)
            : InvokeReaderAsync("firstEpubPage");

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Home")
        {
            await GoToFirstPageAsync();
        }
        else if (e.Key == "ArrowLeft")
        {
            await PrevPage();
        }
        else if (e.Key == "ArrowRight")
        {
            await NextPage();
        }
    }

    private async Task HandlePdfKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Home")
        {
            await GoToFirstPageAsync();
        }
        else if (e.Key is "ArrowLeft" or "PageUp")
        {
            await PrevPdfPage();
        }
        else if (e.Key is "ArrowRight" or "PageDown")
        {
            await NextPdfPage();
        }
    }

    /// <summary>Fire-and-forget safe interop for reader controls (circuit may drop).</summary>
    private async Task InvokeReaderAsync(string method, params object?[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                await JS.InvokeVoidAsync($"shelfwardenReader.{method}");
            }
            else
            {
                await JS.InvokeVoidAsync($"shelfwardenReader.{method}", args);
            }
        }
        catch (JSDisconnectedException) { }
    }

    private async Task LoadBookmarksAsync()
    {
        var result = await BookmarkService.ListAsync(Id);
        if (result.IsSuccess)
        {
            bookmarks.Clear();
            bookmarks.AddRange(result.Value);
        }
    }

    private async Task MountEpubAsync(string fileUrl)
    {
        string? resumeCfi = null;
        var progressResult = await BookService.GetProgressAsync(Id);
        if (progressResult.IsSuccess && progressResult.Value is BookProgressDto dto)
        {
            resumeCfi = dto.Location;
            progressPercent = dto.Percentage;
        }

        selfRef = DotNetObjectReference.Create(this);
        await JS.InvokeVoidAsync("shelfwardenReader.mountEpub", "epub-viewer", fileUrl, selfRef, resumeCfi);
    }

    private async Task MountPdfAsync(string fileUrl)
    {
        int? resumePage = null;
        var progressResult = await BookService.GetProgressAsync(Id);
        if (progressResult.IsSuccess && progressResult.Value is BookProgressDto dto)
        {
            progressPercent = dto.Percentage;
            lastSavedAt = dto.LastReadAt;
            resumePage = dto.PageNumber;
        }

        selfRef = DotNetObjectReference.Create(this);
        var mount = await JS.InvokeAsync<PdfMountResult>(
            "shelfwardenReader.mountPdf", "pdf-viewer", fileUrl, selfRef, resumePage);
        pdfPageCount = mount.PageCount;
        pdfCurrentPage = resumePage ?? 1;
    }

    private Task NextPage() => InvokeReaderAsync("nextPage");

    private Task NextPdfPage() => InvokeReaderAsync("nextPdfPage");

    private Task PrevPage() => InvokeReaderAsync("prevPage");

    private Task PrevPdfPage() => InvokeReaderAsync("prevPdfPage");

    private void ResetTransientReaderState()
    {
        jsMounted = false;
        pdfPageCount = 0;
        pdfCurrentPage = 1;
        progressPercent = null;
        lastSavedAt = null;
        lastSavedCfi = null;
        bookmarkError = null;
        showBookmarks = false;
        bookmarks.Clear();
    }

    private Task ResetZoomAsync() => InvokeReaderAsync("resetZoom");

    private async Task TearDownReaderInteropAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("shelfwardenReader.disposeEpub");
            await JS.InvokeVoidAsync("shelfwardenReader.disposePdf");
            await JS.InvokeVoidAsync("shelfwardenReader.releaseReaderChromeViewport");
        }
        catch (Exception ex) when (IsBenignJsInteropFailure(ex))
        {
        }
        catch (Exception)
        {
            // Script may not have mounted yet.
        }

        selfRef?.Dispose();
        selfRef = null;
    }

    private void ToggleBookmarks() => showBookmarks = !showBookmarks;

    /// <summary>
    /// Toggling is handled entirely on the JS side (reads/flips/writes localStorage + the
    /// `reader-dark` class on &lt;html&gt;) so there's no Blazor state to fetch on mount or
    /// re-render after — see the comment on the button in Reader.razor.
    /// </summary>
    private Task ToggleReaderDarkModeAsync() => InvokeReaderAsync("toggleDarkMode");

    private Task ZoomInAsync() => InvokeReaderAsync("zoomIn");

    private Task ZoomOutAsync() => InvokeReaderAsync("zoomOut");

    private record PdfMountResult(int PageCount);
    private sealed record EpubLocation(string? Cfi, double? Percent);
}