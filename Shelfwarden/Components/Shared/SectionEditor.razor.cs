using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Shelfwarden.Components.Shared;

public partial class SectionEditor : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime Js { get; set; } = default!;

    [Parameter] public bool IsOpen { get; set; }

    [Parameter] public bool Busy { get; set; }

    [Parameter] public IReadOnlyList<BookSection>? Sections { get; set; }

    [Parameter] public SectionDetectionQuality Quality { get; set; }

    [Parameter] public string? Warning { get; set; }

    [Parameter] public bool SplitByChapter { get; set; }

    [Parameter] public EventCallback<bool> SplitByChapterChanged { get; set; }

    /// <summary>True when the book is a PDF — enables the page-marker tab for manual chapter breaks.</summary>
    [Parameter] public bool IsPdf { get; set; }

    /// <summary>Book id used to stream the PDF into the page-marker view (<c>/files/{id}</c>).</summary>
    [Parameter] public int BookId { get; set; }

    /// <summary>Raised when the section list is rebuilt from manual page markers.</summary>
    [Parameter] public EventCallback<List<BookSection>> SectionsChanged { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    [Parameter] public EventCallback OnContinue { get; set; }

    private readonly string pickerContainerId = $"pdf-chapter-picker-{Guid.NewGuid():N}";
    private DotNetObjectReference<SectionEditor>? selfRef;
    private bool wasOpen;
    private bool pageMarkerMode;
    private bool pickerMountPending;
    private bool pickerMounted;
    private int pdfPageCount;

    private bool PageMarkerMode => IsPdf && pageMarkerMode;

    private int IncludedCount => Sections?.Count(s => s.IsIncluded) ?? 0;

    /// <summary>Number of output files a split generation would produce given the current boundaries.</summary>
    private int FileCount
    {
        get
        {
            if (Sections is null)
            {
                return 0;
            }

            int count = 0;
            bool first = true;
            foreach (var section in Sections.Where(s => s.IsIncluded))
            {
                if (first || section.IsChapterBoundary)
                {
                    count++;
                }
                first = false;
            }
            return count;
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        // The modal was dismissed (parent set IsOpen=false): tear down any live PDF render.
        if (wasOpen && !IsOpen)
        {
            await DisposePickerAsync();
            pageMarkerMode = false;
            pickerMounted = false;
        }
        wasOpen = IsOpen;
    }

    private void SetIncluded(BookSection section, ChangeEventArgs e)
        => section.IsIncluded = e.Value is true;

    private void SetBoundary(BookSection section, ChangeEventArgs e)
        => section.IsChapterBoundary = e.Value is true;

    private async Task OnSplitToggled(ChangeEventArgs e)
    {
        SplitByChapter = e.Value is true;
        await SplitByChapterChanged.InvokeAsync(SplitByChapter);
    }

    private void SelectAll()
    {
        if (Sections is null)
        {
            return;
        }

        foreach (var section in Sections)
        {
            section.IsIncluded = true;
        }
    }

    private void DeselectAll()
    {
        if (Sections is null)
        {
            return;
        }

        foreach (var section in Sections)
        {
            section.IsIncluded = false;
        }
    }

    private void SelectChaptersOnly()
    {
        if (Sections is null)
        {
            return;
        }

        foreach (var section in Sections)
        {
            section.IsIncluded = section.Kind == SectionKind.Chapter;
        }
    }

    private async Task ShowListMode()
    {
        if (!pageMarkerMode)
        {
            return;
        }

        pageMarkerMode = false;
        await DisposePickerAsync();
    }

    private void ShowPageMarkerMode()
    {
        if (pageMarkerMode)
        {
            return;
        }

        pageMarkerMode = true;
        pickerMountPending = true;
        // Splitting per chapter is the whole point of marking chapters, so turn it on for them.
        if (!SplitByChapter)
        {
            SplitByChapter = true;
            _ = SplitByChapterChanged.InvokeAsync(true);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!pickerMountPending || !IsOpen || !PageMarkerMode)
        {
            return;
        }

        pickerMountPending = false;
        await MountPickerAsync();
    }

    private async Task MountPickerAsync()
    {
        selfRef ??= DotNetObjectReference.Create(this);

        // Seed markers from any auto-detected chapter starts (page >= 2) so the user starts from
        // the parser's best guess instead of a blank slate.
        var initial = (Sections ?? [])
            .Where(s => s.StartPage is >= 2)
            .Select(s => s.StartPage!.Value)
            .Distinct()
            .OrderBy(p => p)
            .ToArray();

        try
        {
            var result = await Js.InvokeAsync<PickerMountResult>(
                "shelfwardenChapterPicker.mount", pickerContainerId, $"/files/{BookId}", selfRef, initial);
            pdfPageCount = result.PageCount;
            pickerMounted = true;
        }
        catch (Exception ex) when (ex is JSDisconnectedException or OperationCanceledException)
        {
        }
    }

    private async Task DisposePickerAsync()
    {
        if (!pickerMounted)
        {
            return;
        }

        pickerMounted = false;
        try
        {
            await Js.InvokeVoidAsync("shelfwardenChapterPicker.dispose");
        }
        catch (Exception ex) when (ex is JSDisconnectedException or OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Called from JS whenever the user adds or removes a chapter break. Rebuilds the section
    /// list from the page boundaries and pushes it to the parent.
    /// </summary>
    [JSInvokable]
    public async Task OnBoundariesChanged(int[] boundaries)
    {
        var rebuilt = BuildSectionsFromBoundaries(boundaries);
        await SectionsChanged.InvokeAsync(rebuilt);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Turns a sorted set of chapter-start pages into reviewable sections. Pages before the first
    /// marker become an excluded "Front matter" section; each marker starts an included chapter.
    /// With no markers the whole document is one included chapter.
    /// </summary>
    private List<BookSection> BuildSectionsFromBoundaries(int[] boundaries)
    {
        int total = pdfPageCount > 0 ? pdfPageCount : (Sections?.Max(s => s.EndPage ?? 1) ?? 1);

        var starts = boundaries
            .Where(p => p >= 2 && p <= total)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        var list = new List<BookSection>();

        if (starts.Count == 0)
        {
            list.Add(new BookSection
            {
                Title = "Whole document",
                StartPage = 1,
                EndPage = total,
                Kind = SectionKind.Chapter,
                IsIncluded = true,
                IsChapterBoundary = true,
            });
            return list;
        }

        if (starts[0] > 1)
        {
            list.Add(new BookSection
            {
                Title = "Front matter",
                StartPage = 1,
                EndPage = starts[0] - 1,
                Kind = SectionKind.FrontMatter,
                IsIncluded = false,
                IsChapterBoundary = true,
            });
        }

        for (int i = 0; i < starts.Count; i++)
        {
            int start = starts[i];
            int end = i + 1 < starts.Count ? starts[i + 1] - 1 : total;
            list.Add(new BookSection
            {
                Title = $"Chapter {i + 1}",
                StartPage = start,
                EndPage = end,
                Kind = SectionKind.Chapter,
                IsIncluded = true,
                IsChapterBoundary = true,
            });
        }

        return list;
    }

    private static string KindLabel(SectionKind kind) => kind switch
    {
        SectionKind.FrontMatter => "Front matter",
        SectionKind.BackMatter => "Back matter",
        _ => "Other",
    };

    private sealed record PickerMountResult(int PageCount);

    public async ValueTask DisposeAsync()
    {
        await DisposePickerAsync();
        selfRef?.Dispose();
        selfRef = null;
    }
}
