using Shelfwarden.Models;

namespace Shelfwarden.Components.Shared;

public partial class EditExtraContentTagsModal : ComponentBase
{
    private readonly List<string> selectedTags = [];
    private string? error;
    private bool saving;
    private List<AdditionalContentTagDto> tagDirectory = [];

    /// <summary>When set, tag suggestions are limited to tags on that author's items.</summary>
    [Parameter] public int? AuthorIdForTagSuggestions { get; set; }

    [Parameter] public IReadOnlyList<string> InitialTagNames { get; set; } = [];
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public IReadOnlyList<int> ItemIds { get; set; } = [];
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public string Title { get; set; } = "Edit tags";

    protected override void OnParametersSet()
    {
        if (!IsOpen)
        {
            return;
        }

        selectedTags.Clear();
        selectedTags.AddRange(InitialTagNames);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!IsOpen)
        {
            return;
        }

        var result = AuthorIdForTagSuggestions is int authorId
            ? await ContentService.ListTagsForAuthorAsync(authorId)
            : await ContentService.ListTagsAsync();
        if (result.IsSuccess)
        {
            tagDirectory = result.Value.ToList();
        }
    }

    private Task AddTagAsync(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return Task.CompletedTask;
        }

        if (!selectedTags.Any(t => string.Equals(t, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            selectedTags.Add(trimmed);
        }

        return Task.CompletedTask;
    }

    private async Task Close()
    {
        if (OnClose.HasDelegate)
        {
            await OnClose.InvokeAsync();
        }
    }

    private async Task SaveAsync()
    {
        if (ItemIds.Count == 0)
        {
            return;
        }

        saving = true;
        error = null;
        try
        {
            var result = await ContentService.SetItemsTagsAsync(new SetAdditionalContentTagsRequest
            {
                ItemIds = ItemIds,
                TagNames = selectedTags.ToList(),
            });

            if (!result.IsSuccess)
            {
                error = result.Errors.FirstOrDefault() ?? "Could not save tags.";
                return;
            }

            await OnSaved.InvokeAsync();
        }
        finally
        {
            saving = false;
        }
    }

    private Task<IReadOnlyList<string>> SearchTagsAsync(string query)
    {
        IEnumerable<AdditionalContentTagDto> q = tagDirectory;
        if (!string.IsNullOrWhiteSpace(query))
        {
            q = q.Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult<IReadOnlyList<string>>(q.Select(t => t.Name).Take(20).ToList());
    }
}