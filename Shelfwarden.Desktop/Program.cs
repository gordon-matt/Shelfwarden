using ElectronNET;
using ElectronNET.API;
using ElectronNET.API.Entities;
using Hangfire;
using Serilog;
using Shelfwarden;
using Shelfwarden.Components;
using Shelfwarden.Desktop;
using Shelfwarden.Infrastructure;
using Shelfwarden.Services;

// ────────────────────────────────────────────────────────────────────────────────
// Desktop entry point. This is the Electron-wrapped variant of Shelfwarden.
// The web/Docker version (Shelfwarden/Program.cs) is intentionally untouched.
// All Razor components, services and static assets are shared via linked items
// in Shelfwarden.Desktop.csproj (Compile Include / Content Include).
//
// Differences from the web build:
//   • Database:Provider is forced to Sqlite (no server-side DB to manage).
//   • Authentication:Provider is forced to None (single local user, no login UI).
//   • The DB and Hangfire SQLite files live under the per-user app data folder
//     (LocalApplicationData) so a non-admin install can write to them.
// ────────────────────────────────────────────────────────────────────────────────

string appDataDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Shelfwarden");
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

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(appDataDir, "logs", "shelfwarden-.log"), rollingInterval: RollingInterval.Day));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllersWithViews();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

builder.Services.AddShelfwardenDatabase(builder.Configuration);
builder.Services.AddShelfwardenRepositories();
builder.Services.AddShelfwardenServices();
var authProvider = builder.Services.AddShelfwardenAuthentication(builder.Configuration);
builder.Services.AddShelfwardenHangfire(builder.Configuration);

// Register Electron services. UseElectron is a no-op when launched as a regular
// ASP.NET Core process (e.g. `dotnet run`), so this same project can also run
// headless during development.
builder.Services.AddElectron();
builder.UseElectron(args, OnElectronAppReadyAsync);

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireAuthorizationFilter()],
});

app.MapRazorComponents<Shelfwarden.Components.App>()
    .AddInteractiveServerRenderMode();

app.MapControllers();

await using (var scope = app.Services.CreateAsyncScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DbInitializer.InitializeAsync(scope.ServiceProvider, authProvider, app.Configuration, logger);
}

app.Run();

// ──────────────────────────────────────────────────────────────────────────────
// Electron callback — invoked by ElectronNET.Core once the host is ready. Opens
// the main browser window pointed at the in-process ASP.NET Core server.
// ──────────────────────────────────────────────────────────────────────────────
static async Task OnElectronAppReadyAsync()
{
    var options = new BrowserWindowOptions
    {
        Show = false,
        Width = 1400,
        Height = 900,
        Title = "Shelfwarden",
    };

    if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
    {
        options.AutoHideMenuBar = true;
    }

    var window = await Electron.WindowManager.CreateWindowAsync(options);
    window.OnReadyToShow += () => window.Show();
    window.OnClosed += () => Electron.App.Quit();
}
