namespace Shelfwarden.Components.Pages;

public partial class Users : ComponentBase
{
    private IAdminUserManagementService? adminUsers;

    private bool identityLoading = true;
    private IReadOnlyList<AdminUserListItem>? identityRows;
    private string? currentUserId;
    private IReadOnlyList<RoleOption>? assignableRoles;

    private IReadOnlyList<UserInfo>? readOnlyUsers;

    private string? bannerError;

    private bool showUserModal;
    private bool userModalCreate;
    private string modalEmail = "";
    private string modalPassword = "";
    private string? modalRole;
    private string? editingUserId;
    private bool modalSaving;
    private string? modalError;

    protected override async Task OnInitializedAsync()
    {
        adminUsers = Services.GetService<IAdminUserManagementService>();

        if (AuthProvider.Provider == Shelfwarden.Services.Auth.AuthProvider.Identity && adminUsers is not null)
        {
            await LoadIdentityGridAsync();
        }
        else
        {
            readOnlyUsers = await UserInfoService.GetAllUsersAsync();
        }
    }

    private async Task LoadIdentityGridAsync()
    {
        if (adminUsers is null)
        {
            return;
        }

        identityLoading = true;
        bannerError = null;
        StateHasChanged();

        var listResult = await adminUsers.ListUsersAsync();
        var currentResult = await adminUsers.GetCurrentAccountAsync();
        var rolesResult = await adminUsers.GetAssignableRolesAsync();

        if (listResult.IsSuccess)
        {
            identityRows = listResult.Value;
        }
        else
        {
            bannerError = FormatResult(listResult);
            identityRows = [];
        }

        if (currentResult.IsSuccess)
        {
            currentUserId = currentResult.Value.Id;
        }

        if (rolesResult.IsSuccess)
        {
            assignableRoles = rolesResult.Value;
            modalRole ??= assignableRoles.FirstOrDefault(r => r.Name == Constants.Roles.User)?.Name
                ?? assignableRoles.FirstOrDefault()?.Name;
        }

        identityLoading = false;
    }

    private void OpenCreateModal()
    {
        userModalCreate = true;
        editingUserId = null;
        modalEmail = "";
        modalPassword = "";
        modalRole = assignableRoles?.FirstOrDefault(r => r.Name == Constants.Roles.User)?.Name
            ?? assignableRoles?.FirstOrDefault()?.Name;
        modalError = null;
        showUserModal = true;
    }

    private void OpenEditModal(AdminUserListItem u)
    {
        userModalCreate = false;
        editingUserId = u.Id;
        modalEmail = u.Email ?? "";
        modalPassword = "";
        modalRole = u.Roles.FirstOrDefault() ?? Constants.Roles.User;
        modalError = null;
        showUserModal = true;
    }

    private void CloseUserModal()
    {
        showUserModal = false;
        modalError = null;
    }

    private async Task SaveUserModalAsync()
    {
        if (adminUsers is null)
        {
            return;
        }

        modalError = null;
        if (string.IsNullOrWhiteSpace(modalEmail))
        {
            modalError = "Email is required.";
            return;
        }

        if (userModalCreate && string.IsNullOrEmpty(modalPassword))
        {
            modalError = "Password is required.";
            return;
        }

        modalSaving = true;
        try
        {
            Ardalis.Result.Result result;
            if (userModalCreate)
            {
                result = await adminUsers.CreateUserAsync(
                    new AdminCreateUserRequest(modalEmail.Trim(), modalPassword, modalRole));
            }
            else if (!string.IsNullOrEmpty(editingUserId))
            {
                result = await adminUsers.UpdateUserAsync(
                    editingUserId,
                    new AdminUpdateUserRequest(modalEmail.Trim(), modalRole));
            }
            else
            {
                modalError = "Nothing to save.";
                return;
            }

            if (result.IsSuccess)
            {
                CloseUserModal();
                await LoadIdentityGridAsync();
            }
            else
            {
                modalError = FormatResult(result);
            }
        }
        finally
        {
            modalSaving = false;
        }
    }

    private async Task ToggleStatusAsync(AdminUserListItem u)
    {
        if (adminUsers is null)
        {
            return;
        }

        string actionWord = u.IsActive ? "disable" : "enable";
        if (!await JS.InvokeAsync<bool>("confirm", $"Are you sure you want to {actionWord} this user?"))
        {
            return;
        }

        var result = await adminUsers.ToggleLockoutAsync(u.Id);
        if (result.IsSuccess)
        {
            await LoadIdentityGridAsync();
        }
        else
        {
            bannerError = FormatResult(result);
        }
    }

    private async Task DeleteAsync(AdminUserListItem u)
    {
        if (adminUsers is null)
        {
            return;
        }

        if (!await JS.InvokeAsync<bool>("confirm", $"Delete user \"{u.Email}\"? This cannot be undone."))
        {
            return;
        }

        var result = await adminUsers.DeleteUserAsync(u.Id);
        if (result.IsSuccess)
        {
            await LoadIdentityGridAsync();
        }
        else
        {
            bannerError = FormatResult(result);
        }
    }

    private static string FormatResult(Ardalis.Result.Result result)
    {
        if (result.ValidationErrors is not null && result.ValidationErrors.Any())
        {
            return string.Join(" ", result.ValidationErrors.Select(e => e.ErrorMessage));
        }

        return string.Join(" ", result.Errors);
    }

    private static string FormatResult<T>(Ardalis.Result.Result<T> result)
    {
        if (result.ValidationErrors is not null && result.ValidationErrors.Any())
        {
            return string.Join(" ", result.ValidationErrors.Select(e => e.ErrorMessage));
        }

        return string.Join(" ", result.Errors);
    }
}