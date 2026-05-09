using Microsoft.AspNetCore.Identity;

namespace Shelfwarden.Infrastructure;

/// <summary>
/// Maps administrator actions to <see cref="UserManager{ApplicationUser}"/>
/// </summary>
public sealed class AdminIdentityUserManagementService(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IUserContextService userContext) : IAdminUserManagementService
{
    private static readonly HashSet<string> AssignableRoles =
    [
        Constants.Roles.Administrator,
        Constants.Roles.User,
    ];

    public async Task<Result<IReadOnlyList<AdminUserListItem>>> ListUsersAsync(
        CancellationToken cancellationToken = default)
    {
        var users = await userManager.Users
            .OrderBy(u => u.Email ?? u.UserName)
            .ToListAsync(cancellationToken);

        var list = new List<AdminUserListItem>(users.Count);
        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var roles = await userManager.GetRolesAsync(user);
            bool isActive = user.LockoutEnd is null || user.LockoutEnd < DateTimeOffset.UtcNow;

            list.Add(new AdminUserListItem(
                user.Id,
                user.UserName ?? user.Id,
                user.Email,
                user.DisplayName ?? user.UserName,
                user.EmailConfirmed,
                isActive,
                roles.OrderBy(r => r).ToList()));
        }

        return Result.Success<IReadOnlyList<AdminUserListItem>>(list);
    }

    public async Task<Result<(string Id, string? Email, string? UserName)>> GetCurrentAccountAsync(
        CancellationToken cancellationToken = default)
    {
        string? id = userContext.GetCurrentUserId();
        if (string.IsNullOrEmpty(id))
        {
            return Result.Unauthorized();
        }

        var user = await userManager.FindByIdAsync(id);
        return user is null ? (Result<(string Id, string? Email, string? UserName)>)Result.NotFound() : Result.Success((user.Id, user.Email, user.UserName));
    }

    public async Task<Result<IReadOnlyList<RoleOption>>> GetAssignableRolesAsync(
        CancellationToken cancellationToken = default)
    {
        var roles = await roleManager.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleOption(r.Id, r.Name ?? ""))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<RoleOption>>(roles);
    }

    public async Task<Result> CreateUserAsync(AdminCreateUserRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Result.Invalid(new ValidationError(nameof(request.Email), "Email is required."));
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            return Result.Invalid(new ValidationError(nameof(request.Password), "Password is required."));
        }

        string role = string.IsNullOrWhiteSpace(request.Role) ? Constants.Roles.User : request.Role!;
        if (!AssignableRoles.Contains(role))
        {
            return Result.Invalid(new ValidationError(nameof(request.Role), $"Role must be one of: {string.Join(", ", AssignableRoles)}."));
        }

        string email = request.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return Result.Invalid(
                createResult.Errors.Select(e => new ValidationError("", e.Description)).ToArray());
        }

        var addRole = await userManager.AddToRoleAsync(user, role);
        if (!addRole.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return Result.Invalid(
                addRole.Errors.Select(e => new ValidationError("", e.Description)).ToArray());
        }

        return Result.Success();
    }

    public async Task<Result> UpdateUserAsync(
        string userId,
        AdminUpdateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        string? currentId = userContext.GetCurrentUserId();
        if (string.Equals(userId, currentId, StringComparison.Ordinal))
        {
            return Result.Invalid(new ValidationError("", "You cannot edit your own account from this page."));
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Result.NotFound("User not found.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Result.Invalid(new ValidationError(nameof(request.Email), "Email is required."));
        }

        string role = string.IsNullOrWhiteSpace(request.Role) ? Constants.Roles.User : request.Role!;
        if (!AssignableRoles.Contains(role))
        {
            return Result.Invalid(new ValidationError(nameof(request.Role), $"Role must be one of: {string.Join(", ", AssignableRoles)}."));
        }

        string email = request.Email.Trim();
        user.Email = email;
        user.UserName = email;

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return Result.Invalid(
                updateResult.Errors.Select(e => new ValidationError("", e.Description)).ToArray());
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        await userManager.RemoveFromRolesAsync(user, currentRoles);
        await userManager.AddToRoleAsync(user, role);

        return Result.Success();
    }

    public async Task<Result> ToggleLockoutAsync(string userId, CancellationToken cancellationToken = default)
    {
        string? currentId = userContext.GetCurrentUserId();
        if (string.Equals(userId, currentId, StringComparison.Ordinal))
        {
            return Result.Invalid(new ValidationError("", "You cannot disable your own account."));
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Result.NotFound("User not found.");
        }

        if (user.LockoutEnd is null || user.LockoutEnd < DateTimeOffset.UtcNow)
        {
            await userManager.SetLockoutEnabledAsync(user, true);
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        }
        else
        {
            await userManager.SetLockoutEndDateAsync(user, null);
        }

        return Result.Success();
    }

    public async Task<Result> DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        string? currentId = userContext.GetCurrentUserId();
        if (string.Equals(userId, currentId, StringComparison.Ordinal))
        {
            return Result.Invalid(new ValidationError("", "You cannot delete your own account."));
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Result.NotFound("User not found.");
        }

        var deleteResult = await userManager.DeleteAsync(user);
        return !deleteResult.Succeeded
            ? Result.Invalid(
                deleteResult.Errors.Select(e => new ValidationError("", e.Description)).ToArray())
            : Result.Success();
    }
}