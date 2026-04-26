# Shelfwarden — Project Context

> This file is the canonical state-of-the-project document. Point Cursor at it when resuming work so the next session has all the architectural decisions, conventions, and roadmap in one place.

## What this is

Shelfwarden is an ebook server / library manager for personal use. Conceptually similar to [Kavita](https://github.com/Kareadita/Kavita), but with several deliberate differences:

- **Ebooks only.** No manga / comics. Supported formats v1: **EPUB** and **PDF**.
- **Flexible file layout.** Books can live in *any* directory under a Library root (except the library root itself). No required folder convention. Author / Series / Genre groupings are optional and live in the database, not in directory structure.
- **Blazor Server UI** with Bootstrap (LibMan, with Bootswatch theme set). Custom CSS avoids hard-coded colors so users can swap themes later.
- **Multi-DB.** SQLite (default), PostgreSQL, SQL Server, MySQL — selected per-instance via configuration.
- **Three auth modes:** ASP.NET Identity (default), Keycloak (OIDC), or **None** (anonymous "default user", primarily for the desktop build).
- **Web + Desktop.** Web app deploys via Docker. Desktop app is the same Blazor app wrapped in [ElectronNET](https://github.com/ElectronNET/Electron.NET), forced to SQLite + None auth so non-technical users can just install and run it.

## Sister projects this borrows from

- `D:\Source\GitHub\MyVideoArchive` — primary code-pattern source (services + Ardalis.Result + Extenso.Data.Entity + Identity/Keycloak + ElectronNET wrapper).
- `D:\Source\GitHub\Kinnect` — secondary reference for the same patterns.
- `D:\Source\GitHub\Extenso` — source for `Extenso.Data.Entity` (`IRepository<T>`, `IDbContextFactory`, `BaseEntity<T>`, `SearchOptions<T>`).
- `D:\Source\GitHub\_3rdParty\Kavita` — reference for ebook server features (scanning, EPUB/PDF parsing, reading progress).

> **Note:** Both MyVideoArchive and Kinnect are MVC + Razor Views, *not* Blazor. The patterns below have been adapted for Blazor Server. The reference projects also use **PostgreSQL only**; multi-provider switching is new ground here.

## Solution layout

```
Shelfwarden/
├── Shelfwarden                        Blazor Server web host
├── Shelfwarden.Core                   Constants, enums, Hangfire filters, shared cross-cutting
├── Shelfwarden.Data                   Entities (POCOs), abstract ApplicationDbContextBase, IEntityTypeConfigurations (provider-agnostic)
├── Shelfwarden.Data.Sqlite            SQLite-specific DbContext + factory + migrations + DI extension (default)
├── Shelfwarden.Data.Sql               SQL Server-specific DbContext + factory + migrations + DI extension
├── Shelfwarden.Data.Npgsql            PostgreSQL-specific DbContext + factory + migrations + DI extension
├── Shelfwarden.Data.MySql             MySQL/MariaDB-specific DbContext + factory + migrations + DI extension
├── Shelfwarden.Models                 DTOs / requests / responses (no EF references)
├── Shelfwarden.Services               Business logic, Ardalis.Result, IRepository<T> consumers, IUserInfoService variants
├── Shelfwarden.Desktop                ElectronNET-wrapped variant of the web project (forces Sqlite + None auth)
└── Shelfwarden.Tests                  xUnit, in-memory EF, Moq
```

All projects target **`net10.0`** with `Nullable=enable`, `ImplicitUsings=enable`, file-scoped namespaces.

### Why provider-specific projects?

Each provider has its own EF Core migrations because EF Core migrations bake in provider-specific types (e.g. `serial` columns on Postgres, `IDENTITY` on SQL Server, `INTEGER PRIMARY KEY AUTOINCREMENT` on SQLite). They are not interchangeable. Each provider project therefore contains:

- A concrete `ApplicationDbContext` subclass of `Shelfwarden.Data.ApplicationDbContextBase` (or just an alias when provider-specific overrides aren't needed).
- An `ApplicationDbContextFactory` implementing `Extenso.Data.Entity.IDbContextFactory`.
- A `Migrations/` folder with that provider's snapshot + migrations.
- A `ServiceCollectionExtensions` with `AddShelfwardenSqliteData()` (etc.) for the host project to call.
- Optional provider-specific `IEntityTypeConfiguration<T>` overrides (e.g. column types).

The shared, provider-agnostic mappings live in `Shelfwarden.Data` and are picked up via `ApplyConfigurationsFromAssembly(typeof(ApplicationDbContextBase).Assembly)`. Provider-specific overrides in the provider project are applied *after* via a second `ApplyConfigurationsFromAssembly(typeof(<ConcreteContext>).Assembly)` so they win on conflicts.

## Configuration keys

The host (`Shelfwarden`) reads these at startup:

```jsonc
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=shelfwarden.db"   // appropriate for the chosen provider
  },
  "Database": {
    "Provider": "Sqlite"     // Sqlite | SqlServer | Npgsql | MySql
  },
  "Authentication": {
    "Provider": "Identity",  // Identity | Keycloak | None
    "Keycloak": {
      "Authority": "...",
      "ClientId": "...",
      "ClientSecret": "...",
      "BackchannelBaseUrl": null,
      "PublicOrigin": null,
      "AdminClientId": null,
      "AdminClientSecret": null
    }
  },
  "SeedAdmin": {
    "Email": "admin@shelfwarden.local",
    "Password": "Admin@123"
  }
}
```

The desktop project hard-codes `Database:Provider=Sqlite` and `Authentication:Provider=None` for end-user simplicity.

### Auth = "None"

When `Authentication:Provider == "None"`:

- No login/registration UI is exposed.
- All requests run as a synthetic "default user" (id `_default`, role `Administrator`).
- `IUserContextService` returns `_default` and `IsAdministrator() == true`.
- `IUserInfoService` is a no-op (`NoneUserInfoService`) returning the default user only.

This is mainly for the desktop build where the user is by definition the only person on the machine.

## Domain model (flat / book-centric)

The model is intentionally simpler than Kavita's `Series → Volume → Chapter → File` hierarchy.

| Entity | Notes |
|--------|-------|
| `ApplicationUser` / `ApplicationRole` | Identity user + role. Used for Identity mode and "None" mode. With Keycloak the local Identity tables still exist for app-only data, but users live in Keycloak. |
| `Library` | A logical collection of books with one or more root `LibraryFolder`s on disk. |
| `LibraryFolder` | Disk path under a library. Books can be at any depth under it. |
| `Book` | The fundamental unit: one ebook file. Has FK to `Library`. Optional FK to `Series`. Has many-to-many `Author`, `Genre`, `Tag`. Stores `FilePath`, `Title`, `FileFormat`, `FileSizeBytes`, `Description`, `Language`, `PublishedOn`, `Isbn`, `PageCount`, `CoverImagePath`, `LastModified`, `LastScanned`, etc. |
| `Author` | Free-standing entity. `Book ↔ Author` is many-to-many via `BookAuthor`. |
| `Series` | Optional. A book can belong to zero or one series. `Book.NumberInSeries` is a nullable decimal so 1.5 etc. works. |
| `Genre` | Many-to-many via `BookGenre`. |
| `Tag` | Many-to-many via `BookTag`. Tags are global (no per-user tags in v1). |
| `BookProgress` | Per `(UserId, BookId)` reading progress: `PageNumber`, `Percentage`, `Location` (CFI for EPUB), `LastReadAt`. |
| `Bookmark` | Per `(UserId, BookId)` named position. |
| `Collection` / `CollectionBook` | User-curated grouping of books (similar to Kavita). |
| `ReadingList` / `ReadingListItem` | Ordered list of books with reading order. |
| `ServerSetting` | Key/value store for first-run wizard, scan settings, theme, etc. |

All entity classes inherit `BaseEntity<int>` from `Extenso.Data.Entity` (which exposes `Id` + `KeyValues`). The Identity entities use `string` IDs (Identity default).

## Conventions

- **Repository pattern:** services accept `IRepository<TEntity>` from `Extenso.Data.Entity`. Use `SearchOptions<T>` with `Query`, `Include`, `OrderBy`, `PageNumber`, `PageSize`. Don't inject `DbContext` directly into services.
- **Result:** services return `Ardalis.Result.Result` / `Result<T>`. Don't throw for control flow. Wrap each service method in `try/catch` and log with `ILogger<T>`.
- **Mapping classes:** put `IEntityTypeConfiguration<T>` in the same `.cs` file as the entity (e.g. `BookMap` next to `Book`). Provider-specific overrides go in a parallel file under each `Shelfwarden.Data.{Provider}\EntityConfigurations\` folder.
- **Global usings:** `Shelfwarden.Data\ProjectUsings.cs` brings in `Extenso.Data.Entity`, `Microsoft.EntityFrameworkCore`, and `Shelfwarden.Data.Entities`.
- **Schemas:** entity tables go in the `app` schema (where the provider supports schemas). SQLite ignores schemas; SQL Server / Postgres / MySQL respect them.
- **Identity columns:** all numeric primary keys are `int` with provider-default auto-increment.
- **Soft delete:** not used in v1 (Kavita doesn't either). Hard delete with `OnDelete(DeleteBehavior.Cascade)` for owned children (e.g. `Bookmark.Book`).

## Auth pattern (mirrors MyVideoArchive)

`Program.cs` reads `Authentication:Provider`:

- `"Identity"` → `AddIdentity<ApplicationUser, ApplicationRole>()` + `AddEntityFrameworkStores<...>()` + `AddDefaultUI()`. `IUserInfoService` → `AspNetIdentityUserInfoService`.
- `"Keycloak"` → `AddAuthentication(Cookie + OIDC)`. `KeycloakBackchannelHandler` for Docker hairpin scenarios. `IUserInfoService` → `KeycloakUserInfoService` (uses Keycloak Admin API).
- `"None"` → custom `NoneAuthenticationHandler` that always authenticates as `_default` with role `Administrator`. `IUserInfoService` → `NoneUserInfoService`.

`IAuthProviderService` exposes the active mode to UI layers (e.g. to hide login menu when mode is `None`).

## Background jobs

Hangfire is used for the scanner. The web app runs a default Hangfire server with the `default` and `critical` queues. Each provider project ships a Hangfire storage dependency:

| Provider | Hangfire storage |
|---------|-------------------|
| SQLite  | `Hangfire.SQLite` |
| SqlServer | `Hangfire.SqlServer` (built-in) |
| Npgsql | `Hangfire.PostgreSql` |
| MySql | `Hangfire.MySqlStorage` |

`Shelfwarden.{Provider}.ServiceCollectionExtensions.AddShelfwarden{Provider}Hangfire(connectionString)` wires up the right storage so the host project doesn't need to know.

## Ebook scanning (planned, not yet implemented)

- A `ScanLibraryJob` (Hangfire) walks each `LibraryFolder` recursively, includes only files matching configured extensions (`.epub`, `.pdf`).
- For each file: hash + last-modified check against existing `Book.FilePath` to skip unchanged.
- New / changed file: open it (VersOne.Epub for EPUB, Docnet for PDF) → extract title, authors, language, description, cover image → persist as `Book` + relationships.
- Author normalization: case-insensitive name match; new authors auto-created.
- Series detection: from EPUB `calibre:series` / `belongs-to-collection` metadata when present, else null.
- Cover images: extracted to `<AppData>/covers/<bookId>.<ext>` and served via a `/covers/{id}` controller.

This is **deferred** to a later phase.

## Current state (Phase 1 + Library/Book CRUD)

Implemented in this session:

- ✅ Solution + 11 csproj files, all targeting `net10.0`.
- ✅ `Shelfwarden.Data` entities + provider-agnostic mappings + abstract `ApplicationDbContextBase`.
- ✅ `Shelfwarden.Data.Sqlite` (concrete, default — used at runtime out of the box).
- ✅ `Shelfwarden.Data.Sql` / `Data.Npgsql` / `Data.MySql` skeletons (DbContext + factory + DI extension; **no migrations yet** — generate when first targeting that provider).
- ✅ `Shelfwarden.Services` with `LibraryService`, `BookService`, `AspNetIdentityUserInfoService`, `KeycloakUserInfoService`, `NoneUserInfoService`, `UserContextService`.
- ✅ `Shelfwarden` (Blazor Server) with: Identity / Keycloak / None auth wiring, DB provider selection by config, basic layout (Bootswatch Flatly default), nav menu, Library admin pages, Book browse page.
- ✅ `Shelfwarden.Desktop` ElectronNET wrapper.
- ✅ `Shelfwarden.Tests` skeleton with one in-memory EF test.
- ✅ `docker-compose.yml` for the web build (Postgres profile + optional Keycloak).

## Roadmap (next sessions)

Done:
- ✅ **Library scanner** — `IScannerService` + Hangfire `scan` queue, EPUB (`VersOne.Epub`) and PDF (`UglyToad.PdfPig`) metadata extraction, cover image persistence, change detection (size + last-modified), author/series/genre auto-resolution.
- ✅ **Search / filters** on `/books` (library, author, series, genre + title query + sort).
- ✅ **Theme picker** — `IServerSettingsService`-backed Bootswatch picker in the nav menu, hot-swaps the stylesheet without a page reload (admin-only).
- ✅ **Per-library scan status indicator** — Combines `Library.LastScannedAt` with live Hangfire monitoring (`IScanStatusService`); badge auto-refreshes every 3s on the libraries page.
- ✅ **Cover serving** — `CoversController` streams `wwwroot`-external cover bytes from `Storage:CoversPath` with 7-day cache.

Next:
1. **EPUB reader** — embedded Blazor reader using one of: `epub.js` via interop, or VersOne.Epub server-side rendering of pages similar to Kavita's approach.
2. **PDF reader** — embedded `pdf.js` viewer.
3. **Reading progress + bookmarks UI.**
4. **Collections + Reading Lists UI.**
5. **First-run wizard** (`/setup`): admin user creation + initial library config (uses the existing `setup.complete` server setting).
6. **Generate the missing migrations** for SQL Server, Postgres, MySQL. SQLite already has its `InitialCreate` migration. To regenerate after model changes:

   ```powershell
   dotnet ef migrations add <Name> -p Shelfwarden.Data.Sqlite -s Shelfwarden -c Shelfwarden.Data.Sqlite.ApplicationDbContext --output-dir Migrations
   ```

   Note: the EF 10.0.6+ tooling needs `Microsoft.EntityFrameworkCore.Design` referenced on the **startup project** (`Shelfwarden`) too, otherwise `dotnet ef` fails with `MissingMethodException: AbstractionsStrings.ArgumentIsEmpty(Object)` (see [efcore#38107](https://github.com/dotnet/efcore/issues/38107)).
7. **Integration tests** for the auth-mode switch, scanner, reader.
8. **Recurring scans** — schedule each library's scan via Hangfire `RecurringJob` instead of (or in addition to) on-demand triggering.

## Build / run cheatsheet

```powershell
# Web (default Sqlite + Identity)
dotnet run --project Shelfwarden

# Web with Postgres + Keycloak (via docker-compose)
docker compose up -d

# Desktop (forces Sqlite + None auth)
dotnet run --project Shelfwarden.Desktop

# Tests
dotnet test
```

## Notes for future Cursor sessions

- The Bootswatch theme is loaded from `wwwroot/lib/bootswatch/dist/<theme>/bootstrap.min.css`. Default is `flatly`. To swap themes, change `<ThemeStylesheet />` in `MainLayout.razor` (driven by `IServerSettingsService` later).
- Avoid baking colors into custom CSS. Use Bootstrap utility classes (`text-primary`, `bg-body-tertiary`, `border-subtle`, etc.) and CSS variables (`--bs-primary`) so theme swaps propagate.
- Keep services provider-agnostic. They depend on `IRepository<T>`, never on `ApplicationDbContext` directly.
- New entities: add the POCO + `IEntityTypeConfiguration` to `Shelfwarden.Data\Entities\<Name>.cs`, add a `DbSet<>` to `ApplicationDbContextBase`, run `dotnet ef migrations add <Name> --project Shelfwarden.Data.Sqlite --startup-project Shelfwarden`.
- When generating migrations for the non-Sqlite providers later, set `Database:Provider` to that provider in `appsettings.Development.json` first so the `--startup-project` resolves the right factory.
