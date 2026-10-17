using Hangfire;
using Heroicsoft.NetForge.Abstractions.Navigation;
using Heroicsoft.NetForge.Abstractions.Setup;
using Heroicsoft.NetForge.Components.Bootstrap;
using Heroicsoft.NetForge.Hangfire;
using LogVue;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using Serilog.Events;
using Shelfwarden.Components;
using Shelfwarden.Infrastructure;
using Shelfwarden.Infrastructure.Opds;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogVue(options =>
    builder.Configuration.GetSection(LogVueOptions.SectionName).Bind(options));

builder.Services.AddSerilog((services, configuration) => configuration
    .MinimumLevel.Information()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.LogVue(services));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddHttpClient();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services.AddNetForgeBootstrapComponents(ui => ui.ThemeStorageKey = Shelfwarden.Constants.ClientThemeLocalStorageKey);
builder.Services.Configure<ForgeShellOptions>(shell =>
{
    shell.Brand = "Shelfwarden";
    shell.LogoUrl = "img/Logo.png";
    shell.LogoDarkUrl = "img/Logo_Light.png";
    shell.MarkUrl = "img/Icon.png";
    shell.SearchPlaceholder = "Search books, authors, series...";
    shell.UserMenuItems.Add(new ForgeNavItem { Title = "E-reader access", Href = "/settings/opds", Icon = "bi-tablet" });
});

// The sidebar is data (counts, shelves, reading lists), so it is supplied per circuit rather than
// as static sections.
builder.Services.AddScoped<IShellNavigation, ShelfwardenNavigation>();

// Bounces every non-exempt request to /setup until the first-run wizard is done. Feed readers can't
// follow a redirect to the wizard, so /opds is exempt (its handler answers 404 until enabled).
builder.Services.AddNetForgeSetupGate(gate => gate.ExemptPrefixes.Add("/opds"));
builder.Services.AddScoped<ISetupStatus, ServerSettingsSetupStatus>();

builder.Services.AddShelfwardenDatabase(builder.Configuration);
builder.Services.AddShelfwardenRepositories();
builder.Services.AddShelfwardenServices();
var authProvider = builder.Services.AddShelfwardenAuthentication(builder.Configuration);

builder.Services.AddShelfwardenOpds(builder.Configuration);
builder.Services.AddShelfwardenHangfire(builder.Configuration);

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

var app = builder.Build();

app.UseSerilogRequestLogging(options =>
    // Same properties as Serilog's default, but with OPDS keys masked out of key-in-URL paths.
    options.GetMessageTemplateProperties = (httpContext, requestPath, elapsedMs, statusCode) =>
    [
        new LogEventProperty("RequestMethod", new ScalarValue(httpContext.Request.Method)),
        new LogEventProperty("RequestPath", new ScalarValue(OpdsLogRedaction.RedactPath(requestPath))),
        new LogEventProperty("StatusCode", new ScalarValue(statusCode)),
        new LogEventProperty("Elapsed", new ScalarValue(elapsedMs)),
    ]);

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

app.MapLogVue().RequireAuthorization(new AuthorizeAttribute
{
    Roles = WellKnownRoles.Administrator
});

// Runs after auth so the wizard can render an authoritative "you're signed in as X" if needed, and
// before MapRazorComponents/MapControllers so it can short-circuit Blazor and MVC routes.
app.UseNetForgeSetupGate();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new AdministratorDashboardAuthorizationFilter()],
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