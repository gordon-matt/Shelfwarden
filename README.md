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

---

## Did I use AI for this?

Yes—shamelessly. Without AI, this project would not exist. It is something I have wanted to build for years but never had the time. Life is busy: work, family, and everything in between. I am a senior developer and have been coding since the mid-2000s—I know what good code looks like and I know what sloppy code looks like. So yes: I used AI as a **productivity tool**, but the **architecture and decisions are mine**, and I am the one maintaining it.

## Support

If you find this project useful, consider supporting its development.

<p align="center">
  <a href="https://buymeacoffee.com/vnmatt">
    <img src="https://github.com/gordon-matt/MyResume/blob/gh-pages/assets/images/BuyMeACoffee_Logo.png"
         alt="Buy Me a Coffee"
         height="40">
  </a>&nbsp;&nbsp;&nbsp;
  <a href="https://www.paypal.com/cgi-bin/webscr?cmd=_donations&business=gordon_matt%40live%2ecom&lc=AU&currency_code=AUD&bn=PP%2dDonationsBF%3abtn_donateCC_LG%2egif%3aNonHosted">
    <img src="https://github.com/gordon-matt/MyResume/blob/gh-pages/assets/images/PayPal_Logo.png"
         alt="Donate with PayPal"
         height="40">
  </a>
</p>

<p align="center">
  Prefer using your phone? Scan the QR code:
</p>

<p align="center">
  <a href="https://buymeacoffee.com/vnmatt">
    <img src="https://github.com/gordon-matt/MyResume/blob/gh-pages/assets/images/BuyMeACoffee_QR.png"
         alt="Buy Me a Coffee QR Code"
         width="180">
  </a>
</p>

<img src="https://komarev.com/ghpvc/?username=gordon-matt&label=GitHub%20Hits%20Since%202025-06-01%3A%20&color=ff0000&style=flat" alt="gordon-matt" />