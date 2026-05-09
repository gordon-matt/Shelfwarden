using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Shelfwarden.Components.Shared;

public partial class AuthorProfileModal : ComponentBase
{
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public int AuthorId { get; set; }
    [Parameter] public string AuthorName { get; set; } = string.Empty;
    [Parameter] public string? CurrentBiography { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }

    private enum ModalTab
    { Manual, Match }

    private ModalTab activeTab = ModalTab.Manual;

    private string manualName = string.Empty;
    private string? manualBiography;
    private byte[]? selectedPhotoBytes;
    private string? selectedPhotoExtension;
    private string? selectedPhotoName;
    private bool isSavingManual;

    private string authorMatchQuery = string.Empty;
    private bool isSearchingOpenLibrary;
    private bool hasSearchedOpenLibrary;
    private bool isImportingOpenLibrary;
    private bool isAwaitingImportConfirmation;
    private IReadOnlyList<OpenLibraryAuthorMatchDto> openLibraryMatches = [];
    private OpenLibraryAuthorMatchDto? pendingMatchSelection;

    private string? statusMessage;
    private string? errorMessage;
    private bool hasLoadedForCurrentOpenState;

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

        authorMatchQuery = AuthorName;
        hasSearchedOpenLibrary = false;
        isAwaitingImportConfirmation = false;
        pendingMatchSelection = null;
        openLibraryMatches = [];
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

        if (tab == ModalTab.Match && !hasSearchedOpenLibrary)
        {
            await SearchOpenLibraryAsync();
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

    private async Task SearchOpenLibraryAsync()
    {
        errorMessage = null;
        statusMessage = null;
        hasSearchedOpenLibrary = true;
        isAwaitingImportConfirmation = false;
        pendingMatchSelection = null;

        if (string.IsNullOrWhiteSpace(authorMatchQuery))
        {
            openLibraryMatches = [];
            errorMessage = "Please enter an author name to search.";
            return;
        }

        isSearchingOpenLibrary = true;
        try
        {
            var result = await AuthorService.SearchOpenLibraryAuthorsAsync(authorMatchQuery, 10);
            openLibraryMatches = result.IsSuccess ? result.Value : [];
            if (!result.IsSuccess)
            {
                errorMessage = "OpenLibrary search failed.";
            }
        }
        finally
        {
            isSearchingOpenLibrary = false;
        }
    }

    private void SelectMatch(OpenLibraryAuthorMatchDto match)
    {
        pendingMatchSelection = match;
        isAwaitingImportConfirmation = false;
        errorMessage = null;
        statusMessage = null;
    }

    private void StartImportConfirmation()
    {
        if (pendingMatchSelection is null) return;
        isAwaitingImportConfirmation = true;
    }

    private void CancelImportConfirmation() => isAwaitingImportConfirmation = false;

    private async Task ConfirmImportAsync()
    {
        if (pendingMatchSelection is null)
        {
            return;
        }

        isImportingOpenLibrary = true;
        errorMessage = null;
        statusMessage = null;
        try
        {
            var result = await AuthorService.ImportFromOpenLibraryAsync(AuthorId, pendingMatchSelection.OpenLibraryId);
            if (!result.IsSuccess)
            {
                errorMessage = "Import failed. Please try a different match.";
                return;
            }

            statusMessage = "OpenLibrary metadata imported.";
            await OnSaved.InvokeAsync();
            await CloseAsync();
        }
        finally
        {
            isImportingOpenLibrary = false;
        }
    }

    private async Task CloseAsync()
    {
        await OnClose.InvokeAsync();
    }
}