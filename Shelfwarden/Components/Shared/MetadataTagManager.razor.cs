using Microsoft.AspNetCore.Components.Web;

namespace Shelfwarden.Components.Shared;

public partial class MetadataTagManager : ComponentBase
{
    public enum TagKind
    {
        Book,
        ExtraContent,
    }

    [Parameter, EditorRequired] public TagKind Kind { get; set; }

    [Parameter] public EventCallback<string?> ErrorChanged { get; set; }

    [Inject] private ITagService BookTagService { get; set; } = null!;
    [Inject] private IAdditionalContentTagService ExtraContentTagService { get; set; } = null!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    private IReadOnlyList<TagDto>? tags;
    private string tagQuery = string.Empty;
    private CancellationTokenSource? tagSearchCts;
    private readonly HashSet<int> selectedTagIds = [];
    private bool tagMergeModalOpen;
    private int tagMergeTargetId;
    private List<TagDto> tagMergeCandidates = [];

    private string EntitySingular => Kind == TagKind.Book ? "tag" : "extra content tag";

    private string EntityPlural => Kind == TagKind.Book ? "tags" : "extra content tags";

    private string MergeRadioName => Kind == TagKind.Book ? "mergePrimaryBookTag" : "mergePrimaryExtraContentTag";

    private TagKind loadedKind;

    protected override async Task OnParametersSetAsync()
    {
        if (loadedKind != Kind)
        {
            loadedKind = Kind;
            tags = null;
            tagQuery = string.Empty;
            selectedTagIds.Clear();
            tagMergeModalOpen = false;
            tagMergeCandidates = [];
        }

        if (tags is null)
        {
            await LoadTagsAsync();
        }
    }

    private async Task LoadTagsAsync()
    {
        if (Kind == TagKind.Book)
        {
            var result = await BookTagService.ListAsync(tagQuery);
            tags = result.IsSuccess ? result.Value : [];
            if (!result.IsSuccess)
            {
                await ReportErrorAsync(result);
            }
        }
        else
        {
            var result = await ExtraContentTagService.ListAsync(tagQuery);
            tags = result.IsSuccess
                ? result.Value.Select(t => new TagDto(t.Id, t.Name)).ToList()
                : [];
            if (!result.IsSuccess)
            {
                await ReportErrorAsync(result);
            }
        }
    }

    private async Task OnTagQueryKeyUp(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await LoadTagsAsync();
            return;
        }

        tagSearchCts?.Cancel();
        tagSearchCts = new CancellationTokenSource();
        var token = tagSearchCts.Token;
        try
        {
            await Task.Delay(250, token);
            if (!token.IsCancellationRequested)
            {
                await LoadTagsAsync();
            }
        }
        catch (TaskCanceledException)
        {
        }
    }

    private void ToggleTagSelection(int tagId, bool include)
    {
        if (include)
        {
            selectedTagIds.Add(tagId);
        }
        else
        {
            selectedTagIds.Remove(tagId);
        }
    }

    private void ClearTagSelection() => selectedTagIds.Clear();

    private async Task OpenTagCreateAsync()
    {
        string? name = await JSRuntime.InvokeAsync<string?>("prompt", $"New {EntitySingular} name:");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (Kind == TagKind.Book)
        {
            var result = await BookTagService.CreateAsync(name);
            if (!result.IsSuccess)
            {
                await ReportErrorAsync(result);
                return;
            }
        }
        else
        {
            var result = await ExtraContentTagService.CreateAsync(name);
            if (!result.IsSuccess)
            {
                await ReportErrorAsync(result);
                return;
            }
        }

        await ClearErrorAsync();
        await LoadTagsAsync();
    }

    private async Task OpenTagRenameAsync(TagDto tag)
    {
        string? name = await JSRuntime.InvokeAsync<string?>("prompt", $"Rename {EntitySingular}:", tag.Name);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), tag.Name, StringComparison.Ordinal))
        {
            return;
        }

        if (Kind == TagKind.Book)
        {
            var result = await BookTagService.UpdateAsync(tag.Id, name);
            if (!result.IsSuccess)
            {
                await ReportErrorAsync(result);
                return;
            }
        }
        else
        {
            var result = await ExtraContentTagService.UpdateAsync(tag.Id, name);
            if (!result.IsSuccess)
            {
                await ReportErrorAsync(result);
                return;
            }
        }

        await ClearErrorAsync();
        await LoadTagsAsync();
    }

    private async Task DeleteTagAsync(TagDto tag)
    {
        if (!await JSRuntime.InvokeAsync<bool>("shelfwarden.confirmDialog",
                $"Delete {EntitySingular} \"{tag.Name}\"? This cannot be undone."))
        {
            return;
        }

        if (Kind == TagKind.Book)
        {
            var result = await BookTagService.DeleteAsync(tag.Id);
            if (!result.IsSuccess)
            {
                await ReportErrorAsync(result);
                return;
            }
        }
        else
        {
            var result = await ExtraContentTagService.DeleteAsync(tag.Id);
            if (!result.IsSuccess)
            {
                await ReportErrorAsync(result);
                return;
            }
        }

        selectedTagIds.Remove(tag.Id);
        await ClearErrorAsync();
        await LoadTagsAsync();
    }

    private async Task DeleteSelectedTagsAsync()
    {
        if (selectedTagIds.Count == 0)
        {
            return;
        }

        if (!await JSRuntime.InvokeAsync<bool>("shelfwarden.confirmDialog",
                $"Delete {selectedTagIds.Count} selected {EntityPlural}? This cannot be undone."))
        {
            return;
        }

        if (Kind == TagKind.Book)
        {
            var result = await BookTagService.DeleteManyAsync(selectedTagIds.ToList());
            if (!result.IsSuccess)
            {
                await ReportErrorAsync(result);
                return;
            }
        }
        else
        {
            var result = await ExtraContentTagService.DeleteManyAsync(selectedTagIds.ToList());
            if (!result.IsSuccess)
            {
                await ReportErrorAsync(result);
                return;
            }
        }

        selectedTagIds.Clear();
        await ClearErrorAsync();
        await LoadTagsAsync();
    }

    private void OpenTagMergeModal()
    {
        if (tags is null || selectedTagIds.Count < 2)
        {
            return;
        }

        tagMergeCandidates = tags
            .Where(t => selectedTagIds.Contains(t.Id))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tagMergeCandidates.Count < 2)
        {
            tagMergeCandidates = [];
            return;
        }

        tagMergeTargetId = tagMergeCandidates[0].Id;
        tagMergeModalOpen = true;
    }

    private void CloseTagMergeModal()
    {
        tagMergeModalOpen = false;
        tagMergeCandidates = [];
    }

    private async Task ConfirmTagMergeAsync()
    {
        var sourceIds = tagMergeCandidates
            .Where(c => c.Id != tagMergeTargetId)
            .Select(c => c.Id)
            .ToList();
        if (sourceIds.Count == 0)
        {
            return;
        }

        string targetName = tagMergeCandidates.First(c => c.Id == tagMergeTargetId).Name;
        if (!await JSRuntime.InvokeAsync<bool>("shelfwarden.confirmDialog",
                $"Merge into \"{targetName}\"? Other selected {EntityPlural} will be deleted."))
        {
            return;
        }

        if (Kind == TagKind.Book)
        {
            var result = await BookTagService.MergeAsync(tagMergeTargetId, sourceIds);
            if (!result.IsSuccess)
            {
                await ReportErrorAsync(result);
                return;
            }
        }
        else
        {
            var result = await ExtraContentTagService.MergeAsync(tagMergeTargetId, sourceIds);
            if (!result.IsSuccess)
            {
                await ReportErrorAsync(result);
                return;
            }
        }

        CloseTagMergeModal();
        selectedTagIds.Clear();
        await ClearErrorAsync();
        await LoadTagsAsync();
    }

    private Task ClearErrorAsync() => ErrorChanged.InvokeAsync(null);

    private Task ReportErrorAsync(Ardalis.Result.Result result) =>
        ErrorChanged.InvokeAsync(FormatResult(result));

    private Task ReportErrorAsync<T>(Ardalis.Result.Result<T> result) =>
        ErrorChanged.InvokeAsync(FormatResult(result));

    private static string FormatResult(Ardalis.Result.Result result) =>
        result.ValidationErrors is not null && result.ValidationErrors.Any()
            ? string.Join(" ", result.ValidationErrors.Select(e => e.ErrorMessage))
            : string.Join(" ", result.Errors);

    private static string FormatResult<T>(Ardalis.Result.Result<T> result) =>
        result.ValidationErrors is not null && result.ValidationErrors.Any()
            ? string.Join(" ", result.ValidationErrors.Select(e => e.ErrorMessage))
            : string.Join(" ", result.Errors);
}
