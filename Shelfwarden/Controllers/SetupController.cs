using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Shelfwarden.Controllers;

/// <summary>
/// Backs the parts of the first-run wizard that need an <see cref="HttpContext"/> available
/// — specifically <see cref="SignInManager{TUser}"/>, which writes the auth cookie. The
/// rest of the wizard flow lives in <c>Setup.razor</c> and calls <see cref="ISetupService"/>
/// directly. All endpoints reject with a redirect to <c>/</c> once setup is complete so they
/// can't be reused after install.
/// </summary>
[AllowAnonymous]
[Route("setup")]
public class SetupController(
    ILogger<SetupController> logger,
    IAuthProviderService authProvider,
    ISetupService setupService,
    IServerSettingsService serverSettings) : Controller
{
    /// <summary>
    /// Identity-mode admin bootstrap. Either creates the admin user (first install) or updates
    /// the credentials of the seeded admin (e.g. the user is replacing the default
    /// <c>admin@shelfwarden.local / Admin123!</c>). Signs the user in on success and redirects
    /// the browser back to the wizard's shelf step.
    /// </summary>
    [HttpPost("identity-admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IdentityAdmin(
        [FromForm] SetupAdminRequest request,
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] SignInManager<ApplicationUser> signInManager,
        CancellationToken cancellationToken)
    {
        if (await IsSetupCompleteAsync(cancellationToken))
        {
            return Redirect("/");
        }

        if (authProvider.Provider != AuthProvider.Identity)
        {
            return RedirectWithError("welcome", "Identity admin step is only available when authentication mode is Identity.");
        }

        if (!ModelState.IsValid)
        {
            return RedirectWithError("admin", "Email and password are required (password must be at least 6 characters).");
        }

        // Reuse the seeded admin if present so we don't end up with two admin users — the
        // typical flow is install → seed creates `admin@shelfwarden.local` → wizard updates it.
        var existingAdmins = await userManager.GetUsersInRoleAsync(Constants.Roles.Administrator);
        var admin = existingAdmins.FirstOrDefault();

        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                EmailConfirmed = true,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? "Administrator" : request.DisplayName,
            };

            var createResult = await userManager.CreateAsync(admin, request.Password);
            if (!createResult.Succeeded)
            {
                return RedirectWithError("admin", string.Join(" ", createResult.Errors.Select(e => e.Description)));
            }

            await userManager.AddToRoleAsync(admin, Constants.Roles.Administrator);
        }
        else
        {
            admin.UserName = request.Email;
            admin.Email = request.Email;
            admin.EmailConfirmed = true;
            admin.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? admin.DisplayName : request.DisplayName;

            var update = await userManager.UpdateAsync(admin);
            if (!update.Succeeded)
            {
                return RedirectWithError("admin", string.Join(" ", update.Errors.Select(e => e.Description)));
            }

            // Replace the password — RemovePasswordAsync + AddPasswordAsync is the supported way
            // to rotate without knowing the old hash.
            if (await userManager.HasPasswordAsync(admin))
            {
                var remove = await userManager.RemovePasswordAsync(admin);
                if (!remove.Succeeded)
                {
                    return RedirectWithError("admin", string.Join(" ", remove.Errors.Select(e => e.Description)));
                }
            }

            var add = await userManager.AddPasswordAsync(admin, request.Password);
            if (!add.Succeeded)
            {
                return RedirectWithError("admin", string.Join(" ", add.Errors.Select(e => e.Description)));
            }
        }

        await signInManager.SignInAsync(admin, isPersistent: true);
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("[Setup] Administrator '{Email}' provisioned and signed in", admin.Email);
        }

        return Redirect("/setup?step=shelf");
    }

    /// <summary>
    /// Handles the wizard's shelf form. Pure HTML form posts go through here so the user
    /// gets a clean redirect after submission (no Blazor state to worry about across reloads).
    /// </summary>
    [HttpPost("shelf")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateShelf(
        [FromForm] string name,
        [FromForm] string folders,
        [FromForm] string? description,
        CancellationToken cancellationToken)
    {
        if (await IsSetupCompleteAsync(cancellationToken))
        {
            return Redirect("/");
        }

        var folderList = (folders ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (string.IsNullOrWhiteSpace(name) || folderList.Count == 0)
        {
            return RedirectWithError("shelf", "A name and at least one folder are required.");
        }

        var result = await setupService.CreateInitialShelfAsync(new CreateShelfRequest
        {
            Name = name.Trim(),
            Description = description?.Trim(),
            Folders = folderList,
        }, cancellationToken);

        return !result.IsSuccess
            ? RedirectWithError("shelf", result.Errors.FirstOrDefault() ?? "Could not create the shelf.")
            : Redirect("/setup?step=done");
    }

    /// <summary>
    /// Marks the wizard complete and sends the user to the dashboard. Idempotent — clicking
    /// "Finish" twice just bounces back to the home page.
    /// </summary>
    [HttpPost("complete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(CancellationToken cancellationToken)
    {
        await setupService.CompleteAsync(cancellationToken);
        return Redirect("/");
    }

    private async Task<bool> IsSetupCompleteAsync(CancellationToken cancellationToken)
    {
        var settings = await serverSettings.GetAsync(cancellationToken);
        return settings.IsSuccess && settings.Value.SetupComplete;
    }

    private RedirectResult RedirectWithError(string step, string error)
        => Redirect($"/setup?step={Uri.EscapeDataString(step)}&error={Uri.EscapeDataString(error)}");
}
