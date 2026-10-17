using System.Net;
using Hangfire;
using Heroicsoft.NetForge.Abstractions.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Shelfwarden.Data.MySql;
using Shelfwarden.Data.Npgsql;
using Shelfwarden.Data.Sql;
using Shelfwarden.Data.Sqlite;
using Shelfwarden.Infrastructure.Opds;
using Shelfwarden.Services.Opds;

namespace Shelfwarden.Infrastructure;

internal static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Wires up the database provider selected by <c>Database:Provider</c> in configuration.
        /// Defaults to SQLite. The provider must match one of the values in
        /// <see cref="DatabaseProviders"/>. The matching <c>Shelfwarden.Data.{Provider}</c>
        /// project is responsible for registering the concrete <c>ApplicationDbContext</c>,
        /// the <see cref="IDbContextFactory"/>, and the abstract <see cref="ApplicationDbContextBase"/>.
        /// </summary>
        public IServiceCollection AddShelfwardenDatabase(IConfiguration configuration)
        {
            string provider = DatabaseProviders.Resolve(
                configuration[DatabaseProviders.ConfigurationKey],
                DatabaseProviders.Sqlite,
                [.. DatabaseProviders.All]);

            return provider switch
            {
                DatabaseProviders.Sqlite => services.AddShelfwardenSqlite(configuration),
                DatabaseProviders.SqlServer => services.AddShelfwardenSqlServer(configuration),
                DatabaseProviders.Npgsql => services.AddShelfwardenNpgsql(configuration),
                DatabaseProviders.MySql => services.AddShelfwardenMySql(configuration),
                _ => throw new InvalidOperationException($"Unknown {DatabaseProviders.ConfigurationKey} '{provider}'."),
            };
        }

        /// <summary>
        /// OPDS catalogue: binds <see cref="OpdsOptions"/> from the <c>Opds</c> section and adds the
        /// <see cref="OpdsAuthenticationHandler"/> scheme alongside whichever interactive auth mode is active.
        /// Whether the catalogue answers at all is the admin-controlled <c>opds.enabled</c> server setting.
        /// </summary>
        public IServiceCollection AddShelfwardenOpds(IConfiguration configuration)
        {
            services.Configure<OpdsOptions>(configuration.GetSection(OpdsOptions.SectionName));
            services.AddSingleton<OpdsAuthThrottle>();
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, OpdsAuthenticationHandler>(OpdsAuthenticationHandler.SchemeName, _ => { });
            return services;
        }

        /// <summary>
        /// Registers Hangfire backed by the same provider as the application database.
        /// For SqlServer and Npgsql, omit or leave <c>ConnectionStrings:Hangfire</c> empty so Hangfire
        /// uses <c>DefaultConnection</c> (Hangfire tables live in the default Hangfire schema).
        /// Set <c>ConnectionStrings:Hangfire</c> only when you intentionally use a different database.
        /// SQLite uses <c>Hangfire:SqlitePath</c> for a separate file.
        /// </summary>
        public IServiceCollection AddShelfwardenHangfire(IConfiguration configuration)
        {
            string provider = DatabaseProviders.Resolve(
                configuration[DatabaseProviders.ConfigurationKey],
                DatabaseProviders.Sqlite,
                [.. DatabaseProviders.All]);
            string? hangfireExplicit = configuration.GetConnectionString("Hangfire");
            string? connectionString = string.IsNullOrWhiteSpace(hangfireExplicit)
                ? configuration.GetConnectionString("DefaultConnection")
                : hangfireExplicit;

            switch (provider)
            {
                case DatabaseProviders.Sqlite:
                    services.AddShelfwardenSqliteHangfire(configuration["Hangfire:SqlitePath"]);
                    break;

                case DatabaseProviders.SqlServer:
                    if (string.IsNullOrEmpty(connectionString))
                    {
                        throw new InvalidOperationException("Hangfire requires a connection string when using SqlServer.");
                    }

                    services.AddShelfwardenSqlServerHangfire(connectionString);
                    break;

                case DatabaseProviders.Npgsql:
                    if (string.IsNullOrEmpty(connectionString))
                    {
                        throw new InvalidOperationException("Hangfire requires a connection string when using Npgsql.");
                    }

                    services.AddShelfwardenNpgsqlHangfire(connectionString);
                    break;

                case DatabaseProviders.MySql:
                    if (string.IsNullOrEmpty(connectionString))
                    {
                        throw new InvalidOperationException("Hangfire requires a connection string when using MySql.");
                    }

                    services.AddShelfwardenMySqlHangfire(connectionString);
                    break;

                default:
                    throw new InvalidOperationException($"Unknown Database:Provider '{provider}'.");
            }

            services.AddHangfireServer(options =>
            {
                options.ServerName = "main";
                options.Queues =
                [
                    Constants.HangfireQueues.Default,
                    Constants.HangfireQueues.Critical,
                ];
            });

            services.AddHangfireServer(options =>
            {
                options.ServerName = "scan";
                options.Queues = [Constants.HangfireQueues.Scan];
                options.WorkerCount = 1; // serialise scans
            });

            // Dedicated TTS server. Audiobook generation is long-running and CPU-bound; one
            // worker prevents two books from competing for the same Kokoro engine instance.
            services.AddHangfireServer(options =>
            {
                options.ServerName = "tts";
                options.Queues = [Constants.HangfireQueues.Tts];
                options.WorkerCount = 1;
            });

            return services;
        }

        /// <summary>
        /// Registers the auth provider selected by <c>Authentication:Provider</c>.
        /// Returns the configured <see cref="AuthProvider"/> so the caller can branch UI / DI.
        /// </summary>
        public AuthProvider AddShelfwardenAuthentication(IConfiguration configuration)
        {
            var provider = services.AddNetForgeAuthentication<ApplicationUser, ApplicationRole, ApplicationDbContextBase>(
                configuration,
                options =>
                {
                    options.Keycloak.RequireHttpsMetadata = false;
                    options.Keycloak.CorrelationCookieSameSite = SameSiteMode.None;
                    options.Keycloak.CorrelationCookieSecurePolicy = CookieSecurePolicy.Always;
                    options.Keycloak.RedirectBrowserToPublicAuthority = true;
                    options.RemoteFailurePath = "/error";
                });

            switch (provider)
            {
                case AuthProvider.Identity:
                    services.AddScoped<IUserInfoService, AspNetIdentityUserInfoService>();
                    services.AddScoped<IAdminUserManagementService, AdminIdentityUserManagementService>();
                    break;

                case AuthProvider.Keycloak:
                    services.AddScoped<IUserInfoService, KeycloakUserInfoService>();
                    services.Configure<ForwardedHeadersOptions>(options =>
                    {
                        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                            | ForwardedHeaders.XForwardedProto
                            | ForwardedHeaders.XForwardedHost;
                        options.KnownProxies.Clear();
                        options.KnownProxies.Add(IPAddress.Parse("127.0.0.1"));
                        options.KnownProxies.Add(IPAddress.Parse("::1"));
                        options.KnownProxies.Add(IPAddress.Parse("172.17.0.1"));
                    });
                    break;

                case AuthProvider.None:
                    services.AddSingleton<IUserInfoService, NoneUserInfoService>();
                    break;
            }

            return provider;
        }
    }
}