namespace Shelfwarden.Components.Pages;

public partial class BookDetail : ComponentBase
{
    private readonly List<BookmarkDto> bookmarks = [];
    private string? addToError;
    private string? addToFlash;
    private bool addToLoaded;
    private AudiobookDto? audiobook;
    private bool audiobookActionBusy;
    private bool audiobookSupported;
    private BookDto? book;
    private IReadOnlyList<AdditionalContentItemDto>? bookExtraContent;
    private IReadOnlyList<CollectionDto>? collections;
    private bool creating;
    private AddToTarget? creatingTarget;
    private List<BookSection>? detectedSections;
    private SectionDetectionQuality detectionQuality;
    private string? detectionWarning;
    private bool enqueuingGenerate;
    private int? extraContentTagScopeAuthorId;
    private string? generateError;
    private bool isAdministrator => UserContext.IsAdministrator();
    private bool loading = true;
    private string newName = string.Empty;
    private string? pendingVoiceConfirmation;
    private Timer? pollTimer;
    private IReadOnlyList<ReadingListDto>? readingLists;
    private bool sectionEditorOpen;
    private bool sectionsBusy;
    private bool showPlayer;
    private bool splitByChapter;
    private bool voicePickerOpen;

    private enum AddToTarget
    {
        Collection,
        ReadingList,
    }

    [Parameter]
    public int Id { get; set; }

    public ValueTask DisposeAsync()
    {
        StopPolling();
        return ValueTask.CompletedTask;
    }

    protected override async Task OnParametersSetAsync()
    {
        loading = true;
        bookExtraContent = null;
        var result = await BookService.GetByIdAsync(Id);
        book = result.IsSuccess ? result.Value : null;

        bookmarks.Clear();
        audiobook = null;
        showPlayer = false;
        audiobookSupported = false;
        StopPolling();

        extraContentTagScopeAuthorId = book?.Authors.FirstOrDefault()?.Id;

        if (book is not null)
        {
            var bmTask = BookmarkService.ListAsync(Id);
            var contentTask = ContentService.GetForBookAsync(Id);
            await Task.WhenAll(bmTask, contentTask);

            if (bmTask.Result.IsSuccess)
            {
                bookmarks.AddRange(bmTask.Result.Value);
            }

            if (contentTask.Result.IsSuccess)
            {
                bookExtraContent = contentTask.Result.Value;
            }

            audiobookSupported = book.FileFormat is EbookFormat.Epub or EbookFormat.Pdf;
            if (audiobookSupported)
            {
                await RefreshAudiobookStatusAsync();
                EnsurePollingMatchesState();
            }
        }

        loading = false;
    }

    private static string DefaultBookmarkTitle(BookmarkDto bm)
        => bm.PageNumber.HasValue ? $"Page {bm.PageNumber}" : "Saved spot";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private static string FormatDuration(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes}m"
            : ts.TotalMinutes >= 1
                ? $"{ts.Minutes}m {ts.Seconds}s"
                : $"{ts.Seconds}s";
    }

    private static string FormatState(AudiobookState state) => state switch
    {
        AudiobookState.Pending => "Queued",
        AudiobookState.Running => "Generating",
        AudiobookState.Completed => "Ready",
        AudiobookState.Failed => "Failed",
        _ => "—",
    };

    /// <summary>
    /// FFmpeg merge + AAC encode after all PCM chunks exist. We key off <see cref="AudiobookDto.CurrentStage"/>
    /// first; if counts show 100% while still running we infer stitching unless the worker
    /// hasn't flipped the label yet (still "Synthesising…").
    /// </summary>
    private static bool IsStitchingPhase(AudiobookDto a)
    {
        if (a.State != AudiobookState.Running)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(a.CurrentStage)
            && a.CurrentStage.Contains("Stitch", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (a.TotalChunks <= 0 || a.CompletedChunks < a.TotalChunks)
        {
            return false;
        }

        // All chunks accounted for but job still running — must be final I/O unless we're in
        // the sub-second window before CurrentStage updates away from Synthesising.
        return string.IsNullOrWhiteSpace(a.CurrentStage) || !a.CurrentStage.Contains("Synthes", StringComparison.OrdinalIgnoreCase);
    }

    private static string StatusBadgeClass(AudiobookState state) => state switch
    {
        AudiobookState.Pending => "text-bg-info",
        AudiobookState.Running => "text-bg-primary",
        AudiobookState.Completed => "text-bg-success",
        AudiobookState.Failed => "text-bg-danger",
        _ => "text-bg-secondary",
    };

    private async Task AddToCollectionAsync(CollectionDto c)
    {
        var result = await CollectionService.AddBookAsync(c.Id, Id);
        FlashAddTo(result.IsSuccess
            ? $"Added to collection \"{c.Name}\"."
            : (result.Errors.FirstOrDefault() ?? "Could not add to collection."), result.IsSuccess);
    }

    private async Task AddToReadingListAsync(ReadingListDto l)
    {
        var result = await ReadingListService.AddBookAsync(l.Id, Id);
        FlashAddTo(result.IsSuccess
            ? $"Added to reading list \"{l.Name}\"."
            : (result.Errors.FirstOrDefault() ?? "Could not add to reading list."), result.IsSuccess);
    }

    private async Task CancelAudiobookGenerationAsync()
    {
        if (audiobook is null)
        {
            return;
        }

        string msg = audiobook.State == AudiobookState.Failed
            ? "Remove this failed attempt so you can try again? Partial files will be deleted."
            : "Cancel audiobook generation? The job will stop and partial files will be removed.";
        if (!await Js.InvokeAsync<bool>("shelfwarden.confirmDialog", msg))
        {
            return;
        }

        audiobookActionBusy = true;
        generateError = null;
        try
        {
            var result = await AudiobookService.CancelAsync(Id);
            if (!result.IsSuccess)
            {
                generateError = result.Errors.FirstOrDefault() ?? "Could not cancel.";
                return;
            }

            audiobook = null;
            showPlayer = false;
            StopPolling();
            await RefreshAudiobookStatusAsync();
            EnsurePollingMatchesState();
        }
        finally
        {
            audiobookActionBusy = false;
        }
    }

    private void CancelCreate()
    {
        creatingTarget = null;
        addToError = null;
    }

    private void CancelGenerate()
    {
        pendingVoiceConfirmation = null;
        generateError = null;
    }

    private void CloseSectionEditor() => sectionEditorOpen = false;

    private void CloseVoicePicker() => voicePickerOpen = false;

    private async Task ConfirmGenerateAsync()
    {
        if (string.IsNullOrEmpty(pendingVoiceConfirmation) || !isAdministrator)
        {
            return;
        }

        enqueuingGenerate = true;
        generateError = null;
        try
        {
            var result = await AudiobookService.GenerateAsync(Id, new GenerateAudiobookRequest
            {
                VoiceName = pendingVoiceConfirmation,
                SplitByChapter = splitByChapter,
                Sections = detectedSections,
            });

            if (!result.IsSuccess)
            {
                generateError = result.Errors.FirstOrDefault() ?? "Could not start generation.";
                return;
            }

            audiobook = result.Value;
            pendingVoiceConfirmation = null;
            showPlayer = false;
            EnsurePollingMatchesState();
        }
        finally
        {
            enqueuingGenerate = false;
        }
    }

    private async Task CreateAndAddAsync()
    {
        if (creatingTarget is null || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        creating = true;
        addToError = null;
        try
        {
            if (creatingTarget == AddToTarget.Collection)
            {
                var created = await CollectionService.CreateAsync(new CreateCollectionRequest { Name = newName });
                if (!created.IsSuccess)
                {
                    addToError = created.Errors.FirstOrDefault() ?? "Could not create collection.";
                    return;
                }
                var added = await CollectionService.AddBookAsync(created.Value.Id, Id);
                if (!added.IsSuccess)
                {
                    addToError = added.Errors.FirstOrDefault() ?? "Created collection but could not add the book.";
                    return;
                }

                SidebarNavRefresh.NotifyNavigationDataChanged();
                // Refresh the dropdown so the new collection appears next time.
                addToLoaded = false;
                await EnsureAddToLoadedAsync();
                creatingTarget = null;
                FlashAddTo($"Created collection \"{created.Value.Name}\" and added this book.", success: true);
            }
            else
            {
                var created = await ReadingListService.CreateAsync(new CreateReadingListRequest { Name = newName });
                if (!created.IsSuccess)
                {
                    addToError = created.Errors.FirstOrDefault() ?? "Could not create reading list.";
                    return;
                }
                var added = await ReadingListService.AddBookAsync(created.Value.Id, Id);
                if (!added.IsSuccess)
                {
                    addToError = added.Errors.FirstOrDefault() ?? "Created list but could not add the book.";
                    return;
                }

                SidebarNavRefresh.NotifyNavigationDataChanged();
                addToLoaded = false;
                await EnsureAddToLoadedAsync();
                creatingTarget = null;
                FlashAddTo($"Created reading list \"{created.Value.Name}\" and added this book.", success: true);
            }
        }
        finally
        {
            creating = false;
        }
    }

    private async Task DeleteAsync()
    {
        var result = await BookService.DeleteAsync(Id);
        if (result.IsSuccess)
        {
            NavigationManager.NavigateTo("books");
        }
    }

    private async Task DeleteChapterAsync(int chapterIndex)
    {
        if (audiobook?.State != AudiobookState.Completed || !audiobook.SplitByChapter)
        {
            return;
        }

        var chapter = audiobook.Chapters?.FirstOrDefault(c => c.Index == chapterIndex);
        string label = chapter is not null ? chapter.Title : $"chapter {chapterIndex + 1}";
        if (!await Js.InvokeAsync<bool>("shelfwarden.confirmDialog",
                $"Remove \"{label}\" from disk? The other chapters will be kept."))
        {
            return;
        }

        audiobookActionBusy = true;
        generateError = null;
        try
        {
            var result = await AudiobookService.DeleteChapterAsync(Id, chapterIndex);
            if (!result.IsSuccess)
            {
                generateError = result.Errors.FirstOrDefault() ?? "Could not delete chapter.";
                return;
            }

            audiobook = result.Value;
            if (audiobook.State == AudiobookState.None)
            {
                showPlayer = false;
            }

            await RefreshAudiobookStatusAsync();
            EnsurePollingMatchesState();
        }
        finally
        {
            audiobookActionBusy = false;
        }
    }

    private async Task DeleteFinishedAudiobookAsync()
    {
        if (audiobook?.State != AudiobookState.Completed)
        {
            return;
        }

        if (!await Js.InvokeAsync<bool>("shelfwarden.confirmDialog",
                "Delete this audiobook file from disk? You can generate again with any voice."))
        {
            return;
        }

        audiobookActionBusy = true;
        generateError = null;
        try
        {
            var result = await AudiobookService.DeleteAsync(Id);
            if (!result.IsSuccess)
            {
                generateError = result.Errors.FirstOrDefault() ?? "Could not delete audiobook.";
                return;
            }

            audiobook = null;
            showPlayer = false;
            StopPolling();
            await RefreshAudiobookStatusAsync();
            EnsurePollingMatchesState();
        }
        finally
        {
            audiobookActionBusy = false;
        }
    }

    /// <summary>
    /// Lazy-load the collections / reading lists the first time the dropdown opens —
    /// the typical book detail visit doesn't touch them at all, so eagerly fetching is wasted work.
    /// </summary>
    private async Task EnsureAddToLoadedAsync()
    {
        if (addToLoaded)
        {
            return;
        }

        addToLoaded = true;

        var collectionsTask = CollectionService.ListAsync();
        var listsTask = ReadingListService.ListAsync();
        await Task.WhenAll(collectionsTask, listsTask);

        var loadedCollections =
            collectionsTask.Result.IsSuccess ? collectionsTask.Result.Value : [];
        collections = isAdministrator
            ? loadedCollections
            : loadedCollections.Where(c => !c.IsGlobal).ToList();
        readingLists = listsTask.Result.IsSuccess ? listsTask.Result.Value : [];
    }

    /// <summary>
    /// Start (or stop) the background poller depending on whether the current audiobook is in
    /// a non-terminal state. We use a Timer rather than a Task.Delay loop so the page is happy
    /// to be disposed without waiting for the next tick.
    /// </summary>
    private void EnsurePollingMatchesState()
    {
        bool needsPolling = audiobook is { State: AudiobookState.Pending or AudiobookState.Running };
        if (needsPolling && pollTimer is null)
        {
            pollTimer = new System.Threading.Timer(async _ =>
            {
                try
                {
                    await InvokeAsync(async () =>
                    {
                        await RefreshAudiobookStatusAsync();
                        EnsurePollingMatchesState();
                        StateHasChanged();
                    });
                }
                catch
                {
                    // Page is being torn down; the timer will be disposed shortly.
                }
            }, state: null, dueTime: TimeSpan.FromSeconds(3), period: TimeSpan.FromSeconds(3));
        }
        else if (!needsPolling && pollTimer is not null)
        {
            StopPolling();
        }
    }

    private void FlashAddTo(string message, bool success)
    {
        if (success)
        {
            addToFlash = message;
            addToError = null;
        }
        else
        {
            addToError = message;
            addToFlash = null;
        }
    }

    private void OnContentItemRenamed(AdditionalContentItemDto renamed)
    {
        if (bookExtraContent is null) return;
        bookExtraContent = bookExtraContent
            .Select(i => i.Id == renamed.Id ? renamed : i)
            .ToList();
    }

    private void OnSectionsContinue()
    {
        sectionEditorOpen = false;
        generateError = null;
        voicePickerOpen = true;
    }

    /// <summary>The section editor rebuilt the plan from manually-placed PDF chapter markers.</summary>
    private void OnSectionsRebuilt(List<BookSection> sections)
    {
        detectedSections = sections;
        detectionQuality = SectionDetectionQuality.Structured;
        detectionWarning = null;
    }

    private void OnSplitByChapterChanged(bool value) => splitByChapter = value;

    private void OnVoiceBack()
    {
        voicePickerOpen = false;
        sectionEditorOpen = true;
    }

    private void OnVoiceSelected(string voiceName)
    {
        voicePickerOpen = false;
        pendingVoiceConfirmation = voiceName;
        generateError = null;
    }

    private async Task RefreshAudiobookStatusAsync()
    {
        var status = await AudiobookService.GetStatusAsync(Id);
        if (status.IsSuccess)
        {
            audiobook = status.Value;
        }
    }

    private async Task RefreshExtraContentAsync()
    {
        var result = await ContentService.GetForBookAsync(Id);
        if (result.IsSuccess)
        {
            bookExtraContent = result.Value;
        }
    }

    private async Task RemoveBookmarkAsync(BookmarkDto bm)
    {
        var result = await BookmarkService.DeleteAsync(bm.Id);
        if (result.IsSuccess)
        {
            bookmarks.Remove(bm);
        }
    }

    private void StartCreate(AddToTarget target)
    {
        creatingTarget = target;
        newName = string.Empty;
        addToError = null;
    }

    /// <summary>
    /// Entry point for both "Generate Audio" and "Regenerate": parse the book into reviewable
    /// sections and open the section editor. Voice selection follows once the user has chosen
    /// what to read and whether to split into chapters.
    /// </summary>
    private async Task StartGenerateFlowAsync()
    {
        if (!isAdministrator)
        {
            return;
        }

        generateError = null;
        detectedSections = null;
        detectionWarning = null;
        detectionQuality = SectionDetectionQuality.Structured;
        splitByChapter = audiobook?.SplitByChapter ?? false;
        sectionEditorOpen = true;
        sectionsBusy = true;
        StateHasChanged();

        var result = await AudiobookService.GetSectionsAsync(Id);
        if (result.IsSuccess)
        {
            detectedSections = result.Value.Sections.ToList();
            detectionQuality = result.Value.Quality;
            detectionWarning = result.Value.Warning;
        }
        else
        {
            generateError = result.Errors.FirstOrDefault() ?? "Could not analyse this book's chapters.";
            sectionEditorOpen = false;
        }

        sectionsBusy = false;
    }

    private void StopPolling()
    {
        pollTimer?.Dispose();
        pollTimer = null;
    }

    private void ToggleListen() => showPlayer = !showPlayer;
}