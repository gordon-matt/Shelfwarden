namespace Shelfwarden.Components.Shared;

public partial class AuthorProfileModal : ComponentBase
{
    /// <summary>Sentinel provider value meaning "query every source".</summary>
    private const string AllProviders = "";

    private ModalTab activeTab = ModalTab.Manual;
    private string authorMatchQuery = string.Empty;
    private string? errorMessage;
    private bool hasLoadedForCurrentOpenState;
    private bool hasSearched;
    private bool isAwaitingImportConfirmation;
    private bool isImporting;
    private bool isSavingManual;
    private bool isSearching;
    private string? manualBiography;
    private string manualName = string.Empty;
    private IReadOnlyList<ExternalAuthorMatchDto> providerMatches = [];
    private IReadOnlyList<string> providers = [];
    private ExternalAuthorMatchDto? photoSource;
    private ExternalAuthorMatchDto? bioSource;
    private string selectedProvider = AllProviders;
    private byte[]? selectedPhotoBytes;
    private string? selectedPhotoExtension;
    private string? selectedPhotoName;
    private string? statusMessage;

    private enum ModalTab
    {
        Manual,
        Match
    }

    [Parameter] public int AuthorId { get; set; }
    [Parameter] public string AuthorName { get; set; } = string.Empty;
    [Parameter] public string? CurrentBiography { get; set; }
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }

    protected override void OnParametersSet()
    {
        if (!IsOpen)
        {
            hasLoadedForCurrentOpenState = false;
            return;
        }

        if (hasLoadedForCurrentOpenState)
        {
            return;
        }

        hasLoadedForCurrentOpenState = true;
        activeTab = ModalTab.Manual;
        manualName = AuthorName;
        manualBiography = CurrentBiography;
        selectedPhotoBytes = null;
        selectedPhotoExtension = null;
        selectedPhotoName = null;
        statusMessage = null;
        errorMessage = null;

        providers = AuthorService.GetAuthorMetadataProviders();
        selectedProvider = AllProviders;
        authorMatchQuery = AuthorName;
        hasSearched = false;
        isAwaitingImportConfirmation = false;
        photoSource = null;
        bioSource = null;
        providerMatches = [];
    }

    private bool HasSelection => photoSource is not null || bioSource is not null;

    private static bool SameMatch(ExternalAuthorMatchDto? a, ExternalAuthorMatchDto b)
        => a is not null && a.Provider == b.Provider && a.ProviderId == b.ProviderId;

    private bool IsPhotoSource(ExternalAuthorMatchDto m) => SameMatch(photoSource, m);

    private bool IsBioSource(ExternalAuthorMatchDto m) => SameMatch(bioSource, m);

    private void CancelImportConfirmation() => isAwaitingImportConfirmation = false;

    private async Task CloseAsync() => await OnClose.InvokeAsync();

    private async Task ConfirmImportAsync()
    {
        if (!HasSelection)
        {
            return;
        }

        isImporting = true;
        errorMessage = null;
        statusMessage = null;
        try
        {
            var result = await AuthorService.ImportAuthorMetadataAsync(
                AuthorId, new AuthorMetadataImportRequest(bioSource, photoSource));
            if (!result.IsSuccess)
            {
                errorMessage = "Import failed. Please try a different match.";
                return;
            }

            statusMessage = "Imported author metadata.";
            await OnSaved.InvokeAsync();
            await CloseAsync();
        }
        finally
        {
            isImporting = false;
        }
    }

    private async Task OnPhotoSelectedAsync(InputFileChangeEventArgs e)
    {
        var file = e.File;
        if (file is null)
        {
            selectedPhotoBytes = null;
            selectedPhotoExtension = null;
            selectedPhotoName = null;
            return;
        }

        await using var stream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        selectedPhotoBytes = ms.ToArray();
        selectedPhotoName = file.Name;
        selectedPhotoExtension = Path.GetExtension(file.Name);
    }

    private async Task SaveManualAsync()
    {
        isSavingManual = true;
        errorMessage = null;
        statusMessage = null;
        try
        {
            var result = await AuthorService.UpdateProfileAsync(
                AuthorId,
                manualBiography,
                selectedPhotoBytes,
                selectedPhotoExtension,
                displayName: manualName);

            if (!result.IsSuccess)
            {
                errorMessage = result.Errors.FirstOrDefault() ?? "Manual update failed.";
                return;
            }

            statusMessage = "Author profile updated.";
            await OnSaved.InvokeAsync();
            await CloseAsync();
        }
        finally
        {
            isSavingManual = false;
        }
    }

    private async Task SearchProviderAsync()
    {
        errorMessage = null;
        statusMessage = null;
        hasSearched = true;
        isAwaitingImportConfirmation = false;
        photoSource = null;
        bioSource = null;

        if (string.IsNullOrWhiteSpace(authorMatchQuery))
        {
            providerMatches = [];
            errorMessage = "Please enter an author name to search.";
            return;
        }

        isSearching = true;
        try
        {
            var result = await AuthorService.SearchAuthorMetadataAsync(
                authorMatchQuery,
                string.IsNullOrEmpty(selectedProvider) ? null : selectedProvider,
                10);
            providerMatches = result.IsSuccess ? result.Value : [];
            if (!result.IsSuccess)
            {
                errorMessage = "Metadata search failed.";
            }
        }
        finally
        {
            isSearching = false;
        }
    }

    private async Task OnProviderChangedAsync(ChangeEventArgs e)
    {
        selectedProvider = e.Value?.ToString() ?? AllProviders;
        if (!string.IsNullOrWhiteSpace(authorMatchQuery))
        {
            await SearchProviderAsync();
        }
    }

    /// <summary>Use this candidate for everything it provides (photo and/or bio).</summary>
    private void SelectWholeMatch(ExternalAuthorMatchDto match)
    {
        if (match.HasPhoto)
        {
            photoSource = match;
        }
        if (match.HasBio)
        {
            bioSource = match;
        }
        ClearSelectionFeedback();
    }

    private void SelectPhotoSource(ExternalAuthorMatchDto match)
    {
        if (!match.HasPhoto)
        {
            return;
        }

        photoSource = IsPhotoSource(match) ? null : match;
        ClearSelectionFeedback();
    }

    private void SelectBioSource(ExternalAuthorMatchDto match)
    {
        if (!match.HasBio)
        {
            return;
        }

        bioSource = IsBioSource(match) ? null : match;
        ClearSelectionFeedback();
    }

    private void ClearSelectionFeedback()
    {
        isAwaitingImportConfirmation = false;
        errorMessage = null;
        statusMessage = null;
    }

    private async Task SetTabAsync(ModalTab tab)
    {
        if (activeTab == tab)
        {
            return;
        }

        activeTab = tab;
        statusMessage = null;
        errorMessage = null;
        isAwaitingImportConfirmation = false;

        if (tab == ModalTab.Match && !hasSearched)
        {
            await SearchProviderAsync();
        }
    }

    private void StartImportConfirmation()
    {
        if (!HasSelection)
        {
            return;
        }

        isAwaitingImportConfirmation = true;
    }
}
