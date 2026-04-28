using System.Net;
using Hangfire;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Shelfwarden.Data.MySql;
using Shelfwarden.Data.Npgsql;
using Shelfwarden.Data.Sql;
using Shelfwarden.Data.Sqlite;

namespace Shelfwarden.Infrastructure;

internal static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Wires up the database provider selected by <c>Database:Provider</c> in configuration.
        /// Defaults to SQLite. The provider must match one of the values in
        /// <see cref="Constants.DatabaseProviders"/>. The matching <c>Shelfwarden.Data.{Provider}</c>
        /// project is responsible for registering the concrete <c>ApplicationDbContext</c>,
        /// the <see cref="IDbContextFactory"/>, and the abstract <see cref="ApplicationDbContextBase"/>.
        /// </summary>
        public IServiceCollection AddShelfwardenDatabase(IConfiguration configuration)
        {
            string provider = configuration["Database:Provider"] ?? Constants.DatabaseProviders.Sqlite;

            return provider switch
            {
                Constants.DatabaseProviders.Sqlite => services.AddShelfwardenSqlite(configuration),
                Constants.DatabaseProviders.SqlServer => services.AddShelfwardenSqlServer(configuration),
                Constants.DatabaseProviders.Npgsql => services.AddShelfwardenNpgsql(configuration),
                Constants.DatabaseProviders.MySql => services.AddShelfwardenMySql(configuration),
                _ => throw new InvalidOperationException(
                    $"Unknown Database:Provider '{provider}'. Valid values: " +
                    "Sqlite, SqlServer, Npgsql, MySql.")
            };
        }

        /// <summary>
        /// Registers Hangfire backed by the same provider as the application database.
        /// Hangfire uses a separate connection string under <c>ConnectionStrings:Hangfire</c>
        /// when present, otherwise it falls back to <c>DefaultConnection</c>. SQLite uses a
        /// file path instead of a real connection string.
        /// </summary>
        public IServiceCollection AddShelfwardenHangfire(IConfiguration configuration)
        {
            string provider = configuration["Database:Provider"] ?? Constants.DatabaseProviders.Sqlite;
            string? connectionString = configuration.GetConnectionString("Hangfire")
                ?? configuration.GetConnectionString("DefaultConnection");

            switch (provider)
            {
                case Constants.DatabaseProviders.Sqlite:
                    services.AddShelfwardenSqliteHangfire(configuration["Hangfire:SqlitePath"]);
                    break;

                case Constants.DatabaseProviders.SqlServer:
                    if (string.IsNullOrEmpty(connectionString))
                    {
                        throw new InvalidOperationException("Hangfire requires a connection string when using SqlServer.");
                    }

                    services.AddShelfwardenSqlServerHangfire(connectionString);
                    break;

                case Constants.DatabaseProviders.Npgsql:
                    if (string.IsNullOrEmpty(connectionString))
                    {
                        throw new InvalidOperationException("Hangfire requires a connection string when using Npgsql.");
                    }

                    services.AddShelfwardenNpgsqlHangfire(connectionString);
                    break;

                case Constants.DatabaseProviders.MySql:
                    throw new InvalidOperationException(
                        "MySQL is not currently supported (pending Pomelo EF Core 10 release).");

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

            return services;
        }

        /// <summary>
        /// Registers the auth provider selected by <c>Authentication:Provider</c>.
        /// Returns the configured <see cref="AuthProvider"/> so the caller can branch UI / DI.
        /// </summary>
        public AuthProvider AddShelfwardenAuthentication(IConfiguration configuration)
        {
            string providerName = configuration["Authentication:Provider"] ?? Constants.AuthProviders.Identity;

            switch (providerName)
            {
                case Constants.AuthProviders.Identity:
                    services.AddSingleton<IAuthProviderService>(new AuthProviderService(AuthProvider.Identity, null));
                    services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
                        {
                            options.Password.RequireDigit = false;
                            options.Password.RequireLowercase = false;
                            options.Password.RequireUppercase = false;
                            options.Password.RequireNonAlphanumeric = false;
                            options.Password.RequiredLength = 6;
                        })
                        .AddEntityFrameworkStores<ApplicationDbContextBase>()
                        .AddDefaultTokenProviders()
                        .AddDefaultUI();
                    services.AddScoped<IUserInfoService, AspNetIdentityUserInfoService>();
                    return AuthProvider.Identity;

                case Constants.AuthProviders.Keycloak:
                    AddKeycloak(services, configuration);
                    services.AddScoped<IUserInfoService, KeycloakUserInfoService>();
                    return AuthProvider.Keycloak;

                case Constants.AuthProviders.None:
                    services.AddSingleton<IAuthProviderService>(new AuthProviderService(AuthProvider.None, null));
                    services.AddAuthentication(NoneAuthenticationHandler.SchemeName)
                        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, NoneAuthenticationHandler>(
                            NoneAuthenticationHandler.SchemeName, _ => { });
                    services.AddAuthorization();

                    // Replace the HTTP-bound IUserContextService with the synthetic one.
                    services.RemoveAll<IUserContextService>();
                    services.AddSingleton<IUserContextService, NoneUserContextService>();
                    services.AddSingleton<IUserInfoService, NoneUserInfoService>();
                    return AuthProvider.None;

                default:
                    throw new InvalidOperationException(
                        $"Unknown Authentication:Provider '{providerName}'. Valid values: " +
                        "Identity, Keycloak, None.");
            }
        }
    }

    private static void AddKeycloak(IServiceCollection services, IConfiguration configuration)
    {
        string? authority = configuration["Authentication:Keycloak:Authority"];
        string? backchannelBase = configuration["Authentication:Keycloak:BackchannelBaseUrl"];

        services.AddSingleton<IAuthProviderService>(new AuthProviderService(AuthProvider.Keycloak, authority));

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.LoginPath = "/auth/login";
            options.AccessDeniedPath = "/access-denied";
        })
        .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            options.Authority = authority;
            if (!string.IsNullOrWhiteSpace(backchannelBase) && !string.IsNullOrWhiteSpace(authority))
            {
                options.BackchannelHttpHandler = new KeycloakBackchannelHandler(authority, backchannelBase);
            }

            options.ClientId = configuration["Authentication:Keycloak:ClientId"];
            options.ClientSecret = configuration["Authentication:Keycloak:ClientSecret"];
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.SaveTokens = true;
            options.GetClaimsFromUserInfoEndpoint = true;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                NameClaimType = "preferred_username",
            };

            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");

            options.RequireHttpsMetadata = false;
            options.CorrelationCookie.SameSite = SameSiteMode.None;
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
            options.NonceCookie.SameSite = SameSiteMode.None;
            options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
            options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;

            string? publicOrigin = configuration["Authentication:Keycloak:PublicOrigin"];
            bool useBackchannel = !string.IsNullOrWhiteSpace(backchannelBase);
            if (!string.IsNullOrWhiteSpace(publicOrigin) || useBackchannel)
            {
                publicOrigin = publicOrigin?.TrimEnd('/');
                options.Events.OnRedirectToIdentityProvider = context =>
                {
                    if (!string.IsNullOrWhiteSpace(publicOrigin))
                    {
                        context.ProtocolMessage.RedirectUri = publicOrigin + "/signin-oidc";
                    }

                    if (useBackchannel && !string.IsNullOrEmpty(authority))
                    {
                        string fullUrl = context.ProtocolMessage.CreateAuthenticationRequestUrl();
                        int queryIndex = fullUrl.IndexOf('?');
                        string query = queryIndex >= 0 ? fullUrl[queryIndex..] : string.Empty;
                        string authUrl = authority.TrimEnd('/') + "/protocol/openid-connect/auth" + query;
                        context.Response.Redirect(authUrl);
                        context.HandleResponse();
                    }
                    return Task.CompletedTask;
                };

                if (!string.IsNullOrWhiteSpace(publicOrigin))
                {
                    options.Events.OnRedirectToIdentityProviderForSignOut = context =>
                    {
                        context.ProtocolMessage.PostLogoutRedirectUri = publicOrigin + "/signout-callback-oidc";
                        return Task.CompletedTask;
                    };
                }
            }

            options.Events.OnRemoteFailure = context =>
            {
                context.Response.Redirect("/error");
                context.HandleResponse();
                return Task.CompletedTask;
            };
        });

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
    }
}