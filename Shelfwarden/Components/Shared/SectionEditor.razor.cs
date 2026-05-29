using Microsoft.AspNetCore.Components.Web;

namespace Shelfwarden.Components.Shared;

public partial class SectionEditor : ComponentBase
{
    [Parameter] public bool IsOpen { get; set; }

    [Parameter] public bool Busy { get; set; }

    [Parameter] public IReadOnlyList<BookSection>? Sections { get; set; }

    [Parameter] public SectionDetectionQuality Quality { get; set; }

    [Parameter] public string? Warning { get; set; }

    [Parameter] public bool SplitByChapter { get; set; }

    [Parameter] public EventCallback<bool> SplitByChapterChanged { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    [Parameter] public EventCallback OnContinue { get; set; }

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

    private static string KindLabel(SectionKind kind) => kind switch
    {
        SectionKind.FrontMatter => "Front matter",
        SectionKind.BackMatter => "Back matter",
        _ => "Other",
    };
}
