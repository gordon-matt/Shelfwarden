# Shelfwarden

Self-hosted ebook library manager — a Kavita-style server, but for ebooks only, with a flexible file layout, multiple database providers, and three auth modes (ASP.NET Identity, Keycloak, or None).

> See [CONTEXT.md](./CONTEXT.md) for the full architecture, conventions and roadmap. Cursor / AI assistants should read it first.

## Features (current)

- Blazor Server UI with Bootstrap (Bootswatch themes).
- Multi-database: SQLite (default), PostgreSQL, SQL Server, MySQL.
- Auth: ASP.NET Identity, Keycloak (OIDC), or None (default user, for desktop).
- Library + Book CRUD (manual entry — scanner is in the roadmap).
- Optional ElectronNET desktop build (forces SQLite + None auth).

## Quick start

### Prerequisites

- .NET 10 SDK
- (Optional) Node.js 22+ — required only for the ElectronNET desktop build.
- (Optional) Docker — for the multi-container web setup with PostgreSQL / Keycloak.

### Run the web app (default: SQLite + Identity)

```powershell
dotnet run --project Shelfwarden
```

Open <https://localhost:5001>. Default admin: `admin@shelfwarden.local` / `Admin@123` (override via `SeedAdmin:Email` / `SeedAdmin:Password` in user secrets).

### Run with Docker (PostgreSQL + optional Keycloak)

```powershell
copy .env.example .env
docker compose up -d
```

The app is at <http://localhost:8080>. The Postgres data lives in the `shelfwarden-pgdata` volume; ebooks go in `shelfwarden-library`. To switch on Keycloak, uncomment the `keycloak` service in `docker-compose.yml`, set `Authentication__Provider=Keycloak` on the `app` service, and run `docker compose up -d` again.

### Run the desktop build

```powershell
dotnet run --project Shelfwarden.Desktop
```

Forces `Database:Provider=Sqlite` and `Authentication:Provider=None`. SQLite database lives next to the executable.

### Run tests

```powershell
dotnet test
```

> Note: `Microsoft.NET.Test.Sdk` 18.4.0 ships a `testhost.exe` pinned to `Microsoft.NETCore.App` 10.0.7. If you hit
> _"You must install or update .NET to run this application"_, install the matching .NET 10 runtime patch from
> <https://dotnet.microsoft.com/download/dotnet/10.0>.

## Configuration

All settings can be overridden via `appsettings.json`, user secrets, or environment variables (using `__` to nest, e.g. `Authentication__Provider`).

| Key | Values | Default |
|-----|--------|---------|
| `Database:Provider` | `Sqlite`, `SqlServer`, `Npgsql`, `MySql` | `Sqlite` |
| `ConnectionStrings:DefaultConnection` | provider-specific connection string | `Data Source=shelfwarden.db` |
| `Authentication:Provider` | `Identity`, `Keycloak`, `None` | `Identity` |

See [CONTEXT.md](./CONTEXT.md) for full configuration details, including Keycloak.

## Project structure

See [CONTEXT.md](./CONTEXT.md). Briefly:

- `Shelfwarden` — Blazor Server host
- `Shelfwarden.Core` — shared constants / cross-cutting
- `Shelfwarden.Data` — entities + provider-agnostic mappings
- `Shelfwarden.Data.{Sqlite,Sql,Npgsql,MySql}` — provider-specific DbContext + migrations
- `Shelfwarden.Models` — DTOs / requests / responses
- `Shelfwarden.Services` — business logic
- `Shelfwarden.Desktop` — ElectronNET wrapper
- `Shelfwarden.Tests` — xUnit tests
