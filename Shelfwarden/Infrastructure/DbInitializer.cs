using Microsoft.AspNetCore.Identity;

namespace Shelfwarden.Infrastructure;

/// <summary>
/// Bootstraps the database when the app starts:
/// - Applies pending EF migrations (or <c>EnsureCreated</c> when running on InMemory).
/// - Seeds the built-in roles.
/// - When ASP.NET Identity is active, seeds a default administrator from configuration
///   (<c>Seed:Admin:UserName</c> / <c>Seed:Admin:Email</c> / <c>Seed:Admin:Password</c>) on
///   first run if no admin exists yet.
/// - When Keycloak is active, ensures <c>setup.complete</c> is true so the first-run wizard is
///   skipped (admins are managed in Keycloak; shelves are added after sign-in).
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, AuthProvider authProvider, IConfiguration configuration, ILogger logger)
    {
        var context = services.GetRequiredService<ApplicationDbContextBase>();
        if (context.Database.IsRelational())
        {
            await context.Database.MigrateAsync();
        }
        else
        {
            await context.Database.EnsureCreatedAsync();
        }

        if (authProvider == AuthProvider.Identity)
        {
            await SeedRolesAsync(services, logger);
            await SeedAdminAsync(services, configuration, logger);
        }
        else if (authProvider == AuthProvider.Keycloak)
        {
            await BypassSetupAsync(context, logger);
        }
    }

    /// <summary>
    /// First-run wizard UI does not run reliably with external OIDC + Blazor Server; set the flag
    /// up-front so Keycloak deployments go straight to the app after sign-in.
    /// </summary>
    private static async Task BypassSetupAsync(ApplicationDbContextBase context, ILogger logger)
    {
        var row = await context.ServerSettings
            .FirstOrDefaultAsync(s => s.Key == Constants.ServerSettingKeys.SetupComplete);

        if (row is null)
        {
            await context.ServerSettings.AddAsync(new ServerSetting
            {
                Key = Constants.ServerSettingKeys.SetupComplete,
                Value = bool.TrueString,
            });
            await context.SaveChangesAsync();
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Authentication: Keycloak — seeded setup.complete=true so the first-run wizard is skipped.");
            }
        }
        else if (!bool.TryParse(row.Value, out bool done) || !done)
        {
            row.Value = bool.TrueString;
            await context.SaveChangesAsync();
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Authentication: Keycloak — set setup.complete=true so the first-run wizard is skipped.");
            }
        }

        ServerSettingsService.InvalidateCache();
    }

    private static async Task SeedRolesAsync(IServiceProvider services, ILogger logger)
    {
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        foreach (string? role in new[] { Constants.Roles.Administrator, Constants.Roles.User })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new ApplicationRole(role));
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Seeded role {Role}", role);
                }
            }
        }
    }

    private static async Task SeedAdminAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        string email = configuration["Seed:Admin:Email"] ?? "admin@shelfwarden.local";
        // ASP.NET Identity's default UI uses UserName == Email at sign-in time
        // (Login.cshtml passes Input.Email to PasswordSignInAsync, which treats it as the username).
        // Keep UserName aligned with Email so users can sign in with their email address.
        string userName = configuration["Seed:Admin:UserName"] ?? email;
        string password = configuration["Seed:Admin:Password"] ?? "Admin123!";

        var existing = await userManager.FindByEmailAsync(email)
            ?? await userManager.FindByNameAsync(userName);
        if (existing is not null)
        {
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = userName,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "Administrator",
        };

        var createResult = await userManager.CreateAsync(admin, password);
        if (!createResult.Succeeded)
        {
            logger.LogError("Failed to seed default admin: {Errors}",
                string.Join(", ", createResult.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(admin, Constants.Roles.Administrator);
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Seeded default administrator '{UserName}'", userName);
        }
    }
}