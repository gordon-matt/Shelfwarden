using Hangfire;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Sejil;
using Serilog;
using Shelfwarden.Components;
using Shelfwarden.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Serilog: console + relational Log table (provider-specific sink matches Database:Provider), like MyVideoArchive.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteToShelfwardenDatabase(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();
builder.Host.UseSejil(writeToProviders: true);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddHttpClient();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

builder.Services.AddShelfwardenDatabase(builder.Configuration);
builder.Services.AddShelfwardenRepositories();
builder.Services.AddShelfwardenServices();
builder.Services.AddScoped<ISidebarNavRefreshService, SidebarNavRefreshService>();
var authProvider = builder.Services.AddShelfwardenAuthentication(builder.Configuration);

switch (authProvider)
{
    case AuthProvider.Keycloak:
        builder.Services.ConfigureSejil(options =>
            options.AuthenticationScheme = CookieAuthenticationDefaults.AuthenticationScheme);
        break;

    case AuthProvider.Identity:
        builder.Services.ConfigureSejil(options =>
            options.AuthenticationScheme = IdentityConstants.ApplicationScheme);
        break;

    case AuthProvider.None:
        builder.Services.ConfigureSejil(options =>
            options.AuthenticationScheme = NoneAuthenticationHandler.SchemeName);
        break;
}

builder.Services.AddShelfwardenHangfire(builder.Configuration);

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

var app = builder.Build();

app.UseSerilogRequestLogging();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}
else
{
    app.UseMigrationsEndPoint();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

app.UseHttpsRedirection();
app.UseStaticFiles();
// Required in Production for Blazor's @Assets[...] (see App.razor); those URLs map through this API,
// not plain UseStaticFiles. MVC-only apps sometimes gate MapStaticAssets on Development; Blazor Web Apps do not.
app.MapStaticAssets();

app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.UseSejil();

// Bounce every non-exempt request to /setup until the first-run wizard is done. Runs after
// auth so the wizard can render an authoritative "you're signed in as X" if needed, and
// before MapRazorComponents/MapControllers so it can short-circuit Blazor and MVC routes.
app.UseMiddleware<SetupRedirectMiddleware>();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireAuthorizationFilter()],
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .WithStaticAssets();

app.MapControllers();

if (authProvider == AuthProvider.Identity)
{
    app.MapRazorPages().WithStaticAssets();
}

await using (var scope = app.Services.CreateAsyncScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await DbInitializer.InitializeAsync(scope.ServiceProvider, authProvider, app.Configuration, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database initialization failed");
        throw;
    }
}

HangfireRecurringJobs.Register();

app.Run();