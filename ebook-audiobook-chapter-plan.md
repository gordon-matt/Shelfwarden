# Ebook Audiobook Feature — Chapter Detection & Audio Splitting
## Cursor AI Context & Implementation Plan

---

## Project Overview

This is a **.NET 10 Blazor application** that converts ebooks to audio (text-to-speech). The feature being added has two goals:

1. **Skip front matter** — Let users exclude copyright pages, table of contents, dedications, prefaces, etc. from the generated audio.
2. **Chapter splitting** — Let users generate one audio file per chapter instead of a single monolithic file.

### Existing Libraries
- **VersOne.Epub** — EPUB parsing
- **PdfPig** — PDF parsing
- Already has a working TTS pipeline that accepts text and produces audio files.

---

## Core Data Model

Introduce a shared `BookSection` record that both the EPUB and PDF parsers produce. The rest of the pipeline (UI + TTS) works exclusively against this abstraction.

```csharp
public record BookSection
{
    public string Title { get; init; } = string.Empty;

    // For PDFs: 1-based page numbers (inclusive)
    public int? StartPage { get; init; }
    public int? EndPage { get; init; }

    // For EPUBs: the spine content file IDs in order
    public List<string> ContentFileIds { get; init; } = [];

    // User-controlled toggles
    public bool IsIncluded { get; set; } = true;
    public bool IsChapterBoundary { get; set; } = true; // if true, starts a new audio file

    // Used for auto-detection of front/back matter
    public SectionKind Kind { get; init; } = SectionKind.Chapter;
}

public enum SectionKind
{
    FrontMatter,   // auto-detected: copyright, TOC, preface, etc.
    Chapter,
    BackMatter,    // auto-detected: index, about the author, etc.
    Unknown
}
```

---

## Parser Architecture

Create an interface that both parsers implement:

```csharp
public interface IEbookSectionParser
{
    Task<List<BookSection>> ParseSectionsAsync(Stream stream);
    Task<string> ExtractTextAsync(Stream stream, BookSection section);
}
```

Implement:
- `EpubSectionParser : IEbookSectionParser`
- `PdfSectionParser : IEbookSectionParser`

Register both in DI, selected by file extension/MIME type at the call site.

---

## EPUB Implementation (`EpubSectionParser`)

EPUBs already contain structured navigation metadata. This should be highly reliable.

### Section Parsing

```csharp
public async Task<List<BookSection>> ParseSectionsAsync(Stream stream)
{
    var book = await EpubReader.ReadBookAsync(stream);
    var sections = new List<BookSection>();

    // book.Navigation gives a tree of EpubNavigationItem
    // Each item has: Title, Link (maps to a content file), NestedItems
    FlattenNavigation(book.Navigation, sections);

    // Auto-classify front/back matter by title keyword matching
    ClassifySections(sections);

    return sections;
}

private void FlattenNavigation(
    IEnumerable<EpubNavigationItem> items,
    List<BookSection> result)
{
    foreach (var item in items)
    {
        result.Add(new BookSection
        {
            Title = item.Title ?? "Untitled",
            ContentFileIds = item.Link != null ? [item.Link.ContentFileId] : [],
            IsIncluded = true,
            IsChapterBoundary = true
        });

        // Recurse into nested chapters (sub-sections)
        if (item.NestedItems?.Any() == true)
            FlattenNavigation(item.NestedItems, result);
    }
}
```

### Text Extraction

```csharp
public async Task<string> ExtractTextAsync(Stream stream, BookSection section)
{
    var book = await EpubReader.ReadBookAsync(stream);
    var sb = new StringBuilder();

    foreach (var fileId in section.ContentFileIds)
    {
        var contentFile = book.Content.Html.Local
            .FirstOrDefault(f => f.FilePath == fileId);
        if (contentFile == null) continue;

        var html = await contentFile.ReadContentAsTextAsync();
        sb.AppendLine(StripHtml(html)); // use HtmlAgilityPack or Regex
    }

    return sb.ToString().Trim();
}
```

**Note:** If a nav item links to an anchor within a file (e.g. `chapter2.xhtml#section3`), you may need to extract only the portion of that HTML file from that anchor onward. Handle this as a secondary concern — most books link at the file level.

---

## PDF Implementation (`PdfSectionParser`)

PDFs have no guaranteed structure, so use a tiered detection strategy.

### Strategy Waterfall

```
1. Try bookmark/outline tree (PdfPig: document.Structure.Bookmarks)
      → If bookmarks found with 2+ entries: use them
2. Try heading heuristics (font size + position analysis)
      → If headings found: use them (show warning to user)
3. Fallback: return single section = entire document
      → Signal to UI to show the manual editor
```

### Bookmark Parsing (Tier 1)

```csharp
private List<BookSection> ParseFromBookmarks(PdfDocument document)
{
    var bookmarks = document.Structure.Bookmarks;
    if (bookmarks == null || !bookmarks.Any())
        return [];

    var sections = new List<BookSection>();
    var pageCount = document.NumberOfPages;

    var flatList = FlattenBookmarks(bookmarks).ToList();

    for (int i = 0; i < flatList.Count; i++)
    {
        var bm = flatList[i];
        var nextBm = i + 1 < flatList.Count ? flatList[i + 1] : null;

        sections.Add(new BookSection
        {
            Title = bm.Title,
            StartPage = bm.PageNumber,
            EndPage = nextBm?.PageNumber - 1 ?? pageCount,
            IsIncluded = true,
            IsChapterBoundary = true
        });
    }

    ClassifySections(sections);
    return sections;
}
```

### Heading Heuristics (Tier 2)

```csharp
private List<BookSection> ParseFromHeuristics(PdfDocument document)
{
    var sections = new List<BookSection>();
    double? bodyFontSize = EstimateBodyFontSize(document); // median font size

    for (int pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
    {
        var page = document.GetPage(pageNum);
        var words = page.GetWords().ToList();
        if (!words.Any()) continue;

        // Look for a heading: short text, large font, near top of page
        var topWords = words
            .OrderByDescending(w => w.BoundingBox.Top)
            .Take(10)
            .ToList();

        var headingCandidate = topWords
            .Where(w =>
                w.FontSize > (bodyFontSize ?? 12) * 1.3 &&  // 30% larger than body
                w.BoundingBox.Top > page.Height * 0.75)      // in top 25% of page
            .Select(w => w.Text)
            .FirstOrDefault();

        if (headingCandidate != null && LooksLikeChapterHeading(headingCandidate))
        {
            sections.Add(new BookSection
            {
                Title = headingCandidate,
                StartPage = pageNum,
                IsIncluded = true,
                IsChapterBoundary = true
            });
        }
    }

    // Fill in EndPage for each section
    for (int i = 0; i < sections.Count - 1; i++)
        sections[i] = sections[i] with { EndPage = sections[i + 1].StartPage - 1 };
    if (sections.Any())
        sections[^1] = sections[^1] with { EndPage = document.NumberOfPages };

    ClassifySections(sections);
    return sections;
}

private bool LooksLikeChapterHeading(string text)
{
    var patterns = new[]
    {
        @"^chapter\s+\d+",
        @"^chapter\s+(one|two|three|four|five|six|seven|eight|nine|ten)",
        @"^\d+\.\s+\w",
        @"^part\s+(i{1,3}|iv|v|vi|vii|viii|ix|x|\d+)",
    };
    return patterns.Any(p =>
        Regex.IsMatch(text.Trim(), p, RegexOptions.IgnoreCase));
}
```

### Text Extraction

```csharp
public Task<string> ExtractTextAsync(Stream stream, BookSection section)
{
    using var document = PdfDocument.Open(stream);
    var sb = new StringBuilder();

    var start = section.StartPage ?? 1;
    var end = section.EndPage ?? document.NumberOfPages;

    for (int p = start; p <= end; p++)
    {
        var page = document.GetPage(p);
        // ContentOrderTextElements preserves reading order
        var text = string.Join(" ", page.GetWords().Select(w => w.Text));
        sb.AppendLine(text);
    }

    return Task.FromResult(sb.ToString().Trim());
}
```

---

## Front/Back Matter Auto-Classification

Shared across both parsers:

```csharp
private static readonly string[] FrontMatterKeywords =
[
    "copyright", "table of contents", "contents", "acknowledgement",
    "acknowledgments", "foreword", "preface", "dedication", "epigraph",
    "about the author", "also by", "title page", "series page",
    "praise for", "permissions", "legal notice", "disclaimer"
];

private static readonly string[] BackMatterKeywords =
[
    "index", "bibliography", "references", "appendix", "glossary",
    "notes", "endnotes", "about the publisher", "colophon"
];

private void ClassifySections(List<BookSection> sections)
{
    foreach (var section in sections)
    {
        var title = section.Title.ToLowerInvariant().Trim();

        if (FrontMatterKeywords.Any(k => title.Contains(k)))
        {
            section with { Kind = SectionKind.FrontMatter, IsIncluded = false };
            // Note: records are immutable — mutate IsIncluded via a mutable property
            // or use a DTO/class instead of record for user-editable fields
        }
        else if (BackMatterKeywords.Any(k => title.Contains(k)))
        {
            section with { Kind = SectionKind.BackMatter, IsIncluded = false };
        }
    }
}
```

> **Design note:** `IsIncluded` and `IsChapterBoundary` need to be mutable (user-editable). Consider making `BookSection` a `class` instead of a `record`, or split immutable metadata from mutable user preferences.

---

## Blazor UI

### Component: `SectionEditor.razor`

This is the main UI component shown after upload and before TTS generation.

**Features:**
- List all detected sections with checkboxes for `IsIncluded`
- Toggle per-section whether it starts a new audio file (`IsChapterBoundary`)
- "Select All" / "Deselect All" / "Chapters Only" quick-select buttons
- Badge indicating auto-detected front/back matter (shown in muted colour)
- Detection quality warning for PDFs that fell back to heuristics or no detection

```razor
@* SectionEditor.razor *@
<div class="section-editor">

    <div class="toolbar">
        <button @onclick="SelectAll">Select All</button>
        <button @onclick="DeselectAll">Deselect All</button>
        <button @onclick="SelectChaptersOnly">Chapters Only</button>
    </div>

    @if (DetectionWarning != null)
    {
        <div class="alert alert-warning">@DetectionWarning</div>
    }

    <ul class="section-list">
        @foreach (var section in Sections)
        {
            <li class="section-item @(section.Kind == SectionKind.FrontMatter ? "front-matter" : "")">
                <input type="checkbox" @bind="section.IsIncluded" />
                <span class="section-title">@section.Title</span>

                @if (section.Kind != SectionKind.Chapter)
                {
                    <span class="badge badge-muted">@section.Kind</span>
                }

                <label class="chapter-split-toggle">
                    <input type="checkbox" @bind="section.IsChapterBoundary" />
                    New audio file
                </label>
            </li>
        }
    </ul>

    <button class="btn-primary" @onclick="OnGenerate">Generate Audiobook</button>
</div>

@code {
    [Parameter] public List<BookSection> Sections { get; set; } = [];
    [Parameter] public string? DetectionWarning { get; set; }
    [Parameter] public EventCallback OnGenerate { get; set; }

    void SelectAll() => Sections.ForEach(s => s.IsIncluded = true);
    void DeselectAll() => Sections.ForEach(s => s.IsIncluded = false);
    void SelectChaptersOnly() =>
        Sections.ForEach(s => s.IsIncluded = s.Kind == SectionKind.Chapter);
}
```

### Fallback: Manual Editor

When PDF detection fails entirely (returns a single section), show a textarea editor that lets the user paste/edit text and insert `[[Chapter N]]` markers. Parse these markers into sections before passing to TTS.

```csharp
public List<BookSection> ParseManualMarkers(string text)
{
    var pattern = @"\[\[(.+?)\]\]";
    var parts = Regex.Split(text, pattern);
    var sections = new List<BookSection>();

    for (int i = 0; i < parts.Length; i++)
    {
        if (i % 2 == 0) // text between markers
        {
            if (!string.IsNullOrWhiteSpace(parts[i]))
                sections.Add(new BookSection
                {
                    Title = i == 0 ? "Front Matter" : "Section",
                    IsIncluded = true,
                    IsChapterBoundary = true
                    // store extracted text separately
                });
        }
        else // the marker title itself
        {
            if (sections.Any())
                sections[^1] = sections[^1] with { Title = parts[i] };
        }
    }

    return sections;
}
```

---

## TTS Pipeline Changes

The existing TTS pipeline should be updated to accept a list of sections rather than raw text.

```csharp
public async Task<List<AudioResult>> GenerateAudiobookAsync(
    Stream ebookStream,
    List<BookSection> sections,
    IEbookSectionParser parser,
    TtsOptions options)
{
    var results = new List<AudioResult>();
    var currentChunkSections = new List<BookSection>();

    foreach (var section in sections.Where(s => s.IsIncluded))
    {
        if (section.IsChapterBoundary && currentChunkSections.Any())
        {
            // Flush the previous chunk
            results.Add(await RenderChunkAsync(
                ebookStream, currentChunkSections, parser, options));
            currentChunkSections.Clear();
        }

        currentChunkSections.Add(section);
    }

    // Flush final chunk
    if (currentChunkSections.Any())
        results.Add(await RenderChunkAsync(
            ebookStream, currentChunkSections, parser, options));

    return results;
}

private async Task<AudioResult> RenderChunkAsync(
    Stream ebookStream,
    List<BookSection> sections,
    IEbookSectionParser parser,
    TtsOptions options)
{
    var textParts = new List<string>();
    foreach (var section in sections)
        textParts.Add(await parser.ExtractTextAsync(ebookStream, section));

    var fullText = string.Join("\n\n", textParts);
    var chapterTitle = sections.First().Title;

    var audio = await _ttsService.SynthesiseAsync(fullText, options);
    return new AudioResult(chapterTitle, audio);
}

public record AudioResult(string Title, byte[] AudioData);
```

---

## Upload Flow (Page/Component Changes)

Update the existing upload page to insert the section editor step:

```
[Upload Page]
    │
    ├─ User uploads .epub or .pdf
    │
    ├─ Detect format → invoke correct IEbookSectionParser
    │
    ├─ ParseSectionsAsync() → List<BookSection>
    │
    ├─ Auto-classify front/back matter (IsIncluded = false)
    │
    ├─ Show SectionEditor component
    │      (user reviews, adjusts checkboxes)
    │
    ├─ User clicks "Generate"
    │
    ├─ GenerateAudiobookAsync() → List<AudioResult>
    │
    └─ Show download links (one per chapter) or zip all
```

---

## File Naming

Name output audio files from the chapter title:

```csharp
private string SanitiseFilename(string title)
{
    var invalid = Path.GetInvalidFileNameChars();
    var clean = string.Concat(title.Select(c => invalid.Contains(c) ? '_' : c));
    return clean.Trim().TrimEnd('.') + ".mp3"; // or .wav etc.
}
```

If generating multiple files, consider offering a ZIP download.

---

## Edge Cases to Handle

| Scenario | Handling |
|---|---|
| EPUB with no navigation | Fall back to one section per spine content file |
| EPUB nav item links to anchor within file | Extract full file for now; note as future improvement |
| PDF with no bookmarks and no detectable headings | Return single section, show manual editor |
| PDF heuristics find < 2 chapters | Treat as no-detection, show manual editor |
| Section with empty text after extraction | Skip silently from TTS input, still show in UI |
| Very long chapters exceeding TTS API limits | Chunk text internally within `SynthesiseAsync` (existing concern, not new) |
| User deselects all sections | Disable Generate button, show validation message |

---

## Suggested File / Folder Structure

```
/Services
    /Parsing
        IEbookSectionParser.cs
        EpubSectionParser.cs
        PdfSectionParser.cs
        SectionClassifier.cs       ← keyword matching logic
        ManualMarkerParser.cs      ← [[Chapter N]] parsing

/Models
    BookSection.cs
    SectionKind.cs
    AudioResult.cs
    TtsOptions.cs                  ← already exists presumably

/Components
    SectionEditor.razor
    SectionEditor.razor.cs
    ManualSectionEditor.razor      ← fallback textarea editor

/Pages
    Upload.razor                   ← updated to include section step
```

---

## Implementation Order (Suggested)

1. **`BookSection` model + `SectionKind` enum** — foundation everything else builds on
2. **`EpubSectionParser`** — highest value, most reliable, easiest to test
3. **`SectionEditor.razor` UI** — wire up with EPUB parser, get the flow working end-to-end
4. **Updated TTS pipeline** — `GenerateAudiobookAsync` with section list input
5. **`PdfSectionParser` Tier 1** — bookmark detection
6. **`PdfSectionParser` Tier 2** — heuristic heading detection
7. **`ManualSectionEditor`** — fallback for poor PDFs
8. **ZIP download** — bundle multiple audio files
9. **Polish** — loading states, progress indicators, error handling
