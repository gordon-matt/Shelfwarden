using ElectronNET.API;
using ElectronNET.API.Entities;
using Hangfire;
using Sejil;
using Serilog;
using Shelfwarden;
using Shelfwarden.Desktop;
using Shelfwarden.Infrastructure;

// ────────────────────────────────────────────────────────────────────────────────
// Desktop entry point. This is the Electron-wrapped variant of Shelfwarden.
// The web/Docker entry point (Shelfwarden/Program.cs) stays separate; shared infra
// (e.g. SerilogShelfwardenExtensions) lives under Shelfwarden/Infrastructure.
// All Razor components, services and static assets are shared via linked items
// in Shelfwarden.Desktop.csproj (Compile Include / Content Include).
//
// Differences from the web build:
//   • Database:Provider is forced to Sqlite (no server-side DB to manage).
//   • Authentication:Provider is forced to None (single local user, no login UI).
//   • The DB and Hangfire SQLite files live under the per-user app data folder
//     (LocalApplicationData) so a non-admin install can write to them.
//   • An installed build keeps its database under LocalApplicationData\Shelfwarden\desktop.
//     `dotnet run` keeps using LocalApplicationData\Shelfwarden, so a test library
//     does not show up in the installer. Book folders are shelf rows in that
//     database, not appsettings.
// ────────────────────────────────────────────────────────────────────────────────

string appDataRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Shelfwarden");
// Published output includes wwwroot next to the executable. `dotnet run` does not
// (wwwroot is linked from the web project and only copied on publish).
bool isInstalledBuild = File.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot", "css", "site.css"));
string appDataDir = isInstalledBuild ? Path.Combine(appDataRoot, "desktop") : appDataRoot;
Directory.CreateDirectory(appDataDir);

// Hardcode the database / auth choices before configuration is read so that an
// errant appsettings.json copied next to the executable can't accidentally
// switch the desktop app to Postgres or Identity.
var overrides = new Dictionary<string, string?>
{
    ["Database:Provider"] = Constants.DatabaseProviders.Sqlite,
    ["Authentication:Provider"] = Constants.AuthProviders.None,
    ["ConnectionStrings:DefaultConnection"] = $"Data Source={Path.Combine(appDataDir, "shelfwarden.db")}",
    ["Hangfire:SqlitePath"] = Path.Combine(appDataDir, "hangfire.db"),
    ["Storage:CoversPath"] = Path.Combine(appDataDir, "covers"),
};

string webRootPath = SharedAssetLocator.ResolveWebRoot();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = webRootPath,
});

builder.Configuration.AddInMemoryCollection(overrides);

Directory.CreateDirectory(Path.Combine(appDataDir, "logs"));

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(appDataDir, "logs", "shelfwarden-.log"), rollingInterval: RollingInterval.Day)
    .WriteToShelfwardenDatabase(builder.Configuration)
    .CreateLogger();

Log.Information(
    "Shelfwarden data directory: {AppDataDir} (installed build: {Installed})",
    appDataDir,
    isInstalledBuild);

builder.Host.UseSerilog();
builder.Host.UseSejil(writeToProviders: true);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

builder.Services.AddShelfwardenDatabase(builder.Configuration);
builder.Services.AddShelfwardenRepositories();
builder.Services.AddShelfwardenServices();
builder.Services.AddScoped<ISidebarNavRefreshService, SidebarNavRefreshService>();
var authProvider = builder.Services.AddShelfwardenAuthentication(builder.Configuration);

// Desktop forces Authentication:Provider=None — synthetic principal uses this scheme.
builder.Services.ConfigureSejil(options =>
    options.AuthenticationScheme = NoneAuthenticationHandler.SchemeName);

builder.Services.AddShelfwardenHangfire(builder.Configuration);

// Register Electron services. UseElectron is a no-op when launched as a regular
// ASP.NET Core process (e.g. `dotnet run`), so this same project can also run
// headless during development.
builder.Services.AddElectron();
builder.UseElectron(args, OnElectronAppReadyAsync);

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseStaticFiles();
// Required in Production for Blazor's @Assets[...] (logo, theme-init.js, and the other
// fingerprinted scripts in App.razor). Those URLs are not served by UseStaticFiles.
app.MapStaticAssets();

app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.UseSejil();

// Fresh installs have no shelves yet. The wizard is where the user picks book folders.
app.UseMiddleware<SetupRedirectMiddleware>();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireAuthorizationFilter()],
});

app.MapRazorComponents<Shelfwarden.Components.App>()
    .AddInteractiveServerRenderMode()
    .WithStaticAssets();

app.MapControllers();

await using (var scope = app.Services.CreateAsyncScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DbInitializer.InitializeAsync(scope.ServiceProvider, authProvider, app.Configuration, logger);
}

HangfireRecurringJobs.Register();

app.Run();

// ──────────────────────────────────────────────────────────────────────────────
// Electron callback — invoked by ElectronNET.Core once the host is ready. Opens
// the main browser window pointed at the in-process ASP.NET Core server.
// ──────────────────────────────────────────────────────────────────────────────
async Task OnElectronAppReadyAsync()
{
    var options = new BrowserWindowOptions
    {
        Show = false,
        Width = 1400,
        Height = 900,
        Title = "Shelfwarden",
        IsRunningBlazor = true,
    };

    string iconPath = Path.Combine(webRootPath, "img", "Icon.png");
    if (File.Exists(iconPath))
    {
        options.Icon = iconPath;
    }

    if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
    {
        options.AutoHideMenuBar = true;
    }

    var window = await Electron.WindowManager.CreateWindowAsync(options);
    window.OnReadyToShow += window.Show;
}