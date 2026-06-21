using Shelfwarden.Infrastructure;

namespace Shelfwarden.Components.Pages;

public partial class ShelfEdit : ComponentBase
{
    private readonly List<BookListItemDto> bannerSelectedBooks = [];
    private readonly ShelfFormModel model = new();
    private readonly HashSet<string> selectedUserIds = new(StringComparer.Ordinal);
    private int _pickerTargetIndex;
    private bool _showFolderPicker;
    private bool allowRoleAdministrator;
    private bool allowRoleUser;
    private CardHeaderBannerMode bannerMode = CardHeaderBannerMode.RandomCovers;
    private IReadOnlyList<UserInfo>? catalogUsers;
    private List<string> folders = [string.Empty];
    private string? loadError;
    private string? saveError;
    private bool saving;
    private CardBannerSettingsDto? shelfBannerSettings;

    [Parameter]
    public int? Id { get; set; }

    private bool IsCreate => Id is null;

    protected override async Task OnInitializedAsync() => catalogUsers = await UserInfoService.GetAllUsersAsync();

    protected override async Task OnParametersSetAsync()
    {
        if (Id is int id)
        {
            var result = await ShelfService.GetByIdAsync(id);
            if (result.IsSuccess)
            {
                var dto = result.Value;
                model.Name = dto.Name;
                model.Description = dto.Description;
                model.DirectoryStructure = dto.DirectoryStructure;
                model.AlwaysUseFileNameForTitle = dto.AlwaysUseFileNameForTitle;
                model.AlwaysIgnoreAuthor = dto.AlwaysIgnoreAuthor;
                model.AlwaysIgnoreTags = dto.AlwaysIgnoreTags;
                model.AlwaysIgnoreGenres = dto.AlwaysIgnoreGenres;
                model.AssignNewBooksToCollection = dto.AssignNewBooksToCollection;
                model.NewBooksCollectionName = dto.NewBooksCollectionName;
                model.AutoFetchOnlineMetadata = dto.AutoFetchOnlineMetadata;
                folders = dto.Folders.Select(f => f.Path).DefaultIfEmpty(string.Empty).ToList();
                selectedUserIds.Clear();
                foreach (string uid in dto.AllowedUserIds)
                {
                    selectedUserIds.Add(uid);
                }

                allowRoleAdministrator = dto.AllowedRoleNames.Any(r =>
                    string.Equals(r, Constants.Roles.Administrator, StringComparison.OrdinalIgnoreCase));
                allowRoleUser = dto.AllowedRoleNames.Any(r =>
                    string.Equals(r, Constants.Roles.User, StringComparison.OrdinalIgnoreCase));

                shelfBannerSettings = dto.BannerSettings;
                if (shelfBannerSettings is not null)
                {
                    bannerMode = shelfBannerSettings.Mode;
                    await LoadBannerSelectedBooksAsync(id, shelfBannerSettings.SelectedBookIds);
                }
                else
                {
                    bannerMode = CardHeaderBannerMode.RandomCovers;
                    bannerSelectedBooks.Clear();
                }

                loadError = null;
                ShelfEditLogger.LogInformation("[ShelfEdit] Loaded shelf {ShelfId} nameLen={NameLen} folders={FolderCount}",
                    id, dto.Name.Length, dto.Folders.Count);
            }
            else
            {
                loadError = ResultMessages.UserFacing(result, "Shelf not found.");
                shelfBannerSettings = null;
                bannerSelectedBooks.Clear();
                ShelfEditLogger.LogWarning("[ShelfEdit] GetById failed ShelfId={ShelfId} Status={Status} MessageLen={Len}",
                    id, result.Status, loadError?.Length ?? 0);
            }
        }
        else
        {
            selectedUserIds.Clear();
            allowRoleAdministrator = false;
            allowRoleUser = false;
            shelfBannerSettings = null;
            bannerSelectedBooks.Clear();
        }
    }

    private static string DirectoryStructureLabel(DirectoryStructure structure) => structure switch
    {
        DirectoryStructure.Calibre => "Calibre library",
        _ => "Unstructured (any layout)",
    };

    private void AddFolder() => folders.Add(string.Empty);

    private IReadOnlyList<string> BuildRoleNames()
    {
        var list = new List<string>();
        if (allowRoleAdministrator)
        {
            list.Add(Constants.Roles.Administrator);
        }

        if (allowRoleUser)
        {
            list.Add(Constants.Roles.User);
        }

        return list;
    }

    private async Task LoadBannerSelectedBooksAsync(int shelfId, IReadOnlyList<int> ids)
    {
        bannerSelectedBooks.Clear();
        if (ids.Count == 0)
        {
            return;
        }

        // Look up exactly the selected books rather than scanning the whole shelf — the
        // chip widget needs ~12 entries at most.
        var result = await BookService.GetListItemsByIdsAsync(ids);
        if (!result.IsSuccess)
        {
            return;
        }

        var byId = result.Value.ToDictionary(b => b.Id);
        foreach (int bookId in ids)
        {
            if (byId.TryGetValue(bookId, out var b))
            {
                bannerSelectedBooks.Add(b);
            }
        }
    }

    private void LogShelfSaveFailure<T>(string operation, Result<T> result, string? userMessage)
    {
        ShelfEditLogger.LogWarning(
            "[ShelfEdit] {Operation} failed Status={Status} UserFacingLen={MsgLen} WhitespaceOnly={Ws} RawErrors=[{Errors}] Validation=[{Validation}]",
            operation,
            result.Status,
            userMessage?.Length ?? 0,
            string.IsNullOrWhiteSpace(userMessage),
            string.Join(" | ", result.Errors.OfType<string>()),
            string.Join(" | ", result.ValidationErrors.Select(v => v.ErrorMessage)));
        if (!string.IsNullOrWhiteSpace(userMessage))
        {
            int n = Math.Min(userMessage!.Length, 400);
            ShelfEditLogger.LogWarning("[ShelfEdit] User-facing message preview: {Preview}", userMessage[..n]);
        }
    }

    private void OnFolderPickerConfirm(string path)
    {
        if (_pickerTargetIndex >= 0 && _pickerTargetIndex < folders.Count)
            folders[_pickerTargetIndex] = path;
        _showFolderPicker = false;
    }

    private void RemoveFolder(int index)
    {
        if (folders.Count > 1)
        {
            folders.RemoveAt(index);
        }
    }

    private async Task SaveAsync()
    {
        var nonEmpty = folders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).ToList();
        if (nonEmpty.Count == 0)
        {
            saveError = "At least one folder is required.";
            return;
        }

        saving = true;
        saveError = null;
        try
        {
            var roleNames = BuildRoleNames();
            var userIds = selectedUserIds.ToList();

            ShelfEditLogger.LogInformation(
                "[ShelfEdit] Save start IsCreate={Create} NameLen={NameLen} FolderCount={Folders} UserIds={UserCount} Roles={Roles}",
                IsCreate,
                model.Name?.Length ?? 0,
                nonEmpty.Count,
                userIds.Count,
                string.Join(",", roleNames));

            if (Id is int shelfId)
            {
                var result = await ShelfService.UpdateAsync(shelfId, new UpdateShelfRequest
                {
                    Name = model.Name ?? string.Empty,
                    Description = model.Description,
                    Folders = nonEmpty,
                    AllowedUserIds = userIds,
                    AllowedRoleNames = roleNames,
                    CardBannerMode = bannerMode,
                    CardBannerSelectedBookIds = bannerSelectedBooks.Select(b => b.Id).ToList(),
                    AlwaysUseFileNameForTitle = model.AlwaysUseFileNameForTitle,
                    AlwaysIgnoreAuthor = model.AlwaysIgnoreAuthor,
                    AlwaysIgnoreTags = model.AlwaysIgnoreTags,
                    AlwaysIgnoreGenres = model.AlwaysIgnoreGenres,
                    AssignNewBooksToCollection = model.AssignNewBooksToCollection,
                    NewBooksCollectionName = model.NewBooksCollectionName,
                    AutoFetchOnlineMetadata = model.AutoFetchOnlineMetadata,
                });
                if (result.IsSuccess)
                {
                    SidebarNavRefresh.NotifyNavigationDataChanged();
                    NavigationManager.NavigateTo("shelves");
                }
                else
                {
                    saveError = ResultMessages.UserFacing(result, "Could not save the shelf.");
                    LogShelfSaveFailure("Update", result, saveError);
                }
            }
            else
            {
                var result = await ShelfService.CreateAsync(new CreateShelfRequest
                {
                    Name = model.Name ?? string.Empty,
                    Description = model.Description,
                    Folders = nonEmpty,
                    AllowedUserIds = userIds,
                    AllowedRoleNames = roleNames,
                    DirectoryStructure = model.DirectoryStructure,
                    AlwaysUseFileNameForTitle = model.AlwaysUseFileNameForTitle,
                    AlwaysIgnoreAuthor = model.AlwaysIgnoreAuthor,
                    AlwaysIgnoreTags = model.AlwaysIgnoreTags,
                    AlwaysIgnoreGenres = model.AlwaysIgnoreGenres,
                    AssignNewBooksToCollection = model.AssignNewBooksToCollection,
                    NewBooksCollectionName = model.NewBooksCollectionName,
                    AutoFetchOnlineMetadata = model.AutoFetchOnlineMetadata,
                });
                if (result.IsSuccess)
                {
                    SidebarNavRefresh.NotifyNavigationDataChanged();
                    NavigationManager.NavigateTo("shelves");
                }
                else
                {
                    saveError = ResultMessages.UserFacing(result, "Could not save the shelf.");
                    LogShelfSaveFailure("Create", result, saveError);
                }
            }
        }
        catch (Exception ex)
        {
            ShelfEditLogger.LogError(ex, "[ShelfEdit] Save threw unexpectedly IsCreate={Create}", IsCreate);
            saveError = "An unexpected error occurred. Check server logs.";
        }
        finally
        {
            saving = false;
        }
    }

    private async Task<IReadOnlyList<BookListItemDto>> SearchBooksOnThisShelfForBannerAsync(string query)
    {
        if (Id is not int sid)
        {
            return [];
        }

        var result = await BookService.SearchAsync(new BookSearchRequest
        {
            ShelfId = sid,
            Query = string.IsNullOrWhiteSpace(query) ? null : query,
            Page = 1,
            PageSize = 20,
        });
        return result.IsSuccess ? result.Value.Items : [];
    }

    private void ShowFolderPicker(int index)
    {
        _pickerTargetIndex = index;
        _showFolderPicker = true;
    }

    private void ToggleUser(string userId, bool on)
    {
        if (on)
        {
            selectedUserIds.Add(userId);
        }
        else
        {
            selectedUserIds.Remove(userId);
        }
    }

    private async Task UploadThisShelfBannerAsync(IBrowserFile file)
    {
        if (Id is not int sid)
        {
            return;
        }

        await using var s = file.OpenReadStream(2_000_000);
        var result = await ShelfService.UploadCardBannerAsync(sid, s, file.Name, file.Size);
        if (result.IsSuccess)
        {
            await OnParametersSetAsync();
        }
    }

    private sealed class ShelfFormModel
    {
        public bool AlwaysIgnoreAuthor { get; set; }

        public bool AlwaysIgnoreGenres { get; set; }

        public bool AlwaysIgnoreTags { get; set; }

        public bool AlwaysUseFileNameForTitle { get; set; }

        public bool AssignNewBooksToCollection { get; set; }

        public bool AutoFetchOnlineMetadata { get; set; }

        [StringLength(2048)]
        public string? Description { get; set; }

        public DirectoryStructure DirectoryStructure { get; set; } = DirectoryStructure.Unstructured;

        [Required, StringLength(256)]
        public string Name { get; set; } = string.Empty;

        [StringLength(256)]
        public string? NewBooksCollectionName { get; set; }
    }
}