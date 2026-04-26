using Hangfire;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using Shelfwarden.Components;
using Shelfwarden.Infrastructure;
using Shelfwarden.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

builder.Services.AddShelfwardenDatabase(builder.Configuration);
builder.Services.AddShelfwardenRepositories();
builder.Services.AddShelfwardenServices();
var authProvider = builder.Services.AddShelfwardenAuthentication(builder.Configuration);
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
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireAuthorizationFilter()],
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapControllers();

if (authProvider == AuthProvider.Identity)
{
    app.MapRazorPages();
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

app.Run();
