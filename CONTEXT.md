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
- ✅ **Search / filters** on `/books` (library, author, series, genre + title query + sort). Topbar search posts `?q=` straight into the books grid.
- ✅ **Theme picker** — `IServerSettingsService`-backed Bootswatch picker in the topbar, hot-swaps the stylesheet without a page reload (admin-only).
- ✅ **Per-library scan status indicator** — Combines `Library.LastScannedAt` with live Hangfire monitoring (`IScanStatusService`); badge auto-refreshes every 3s on the libraries page.
- ✅ **Cover serving** — `CoversController` streams `wwwroot`-external cover bytes from `Storage:CoversPath` with 7-day cache.
- ✅ **Kavita-style UI shell** — Fixed left `Sidebar.razor` (brand + browse + per-user library list + admin), sticky `Topbar.razor` (search + theme picker), `MainLayout.razor` runs them through an `app-shell` flex container. New CSS in `wwwroot/css/site.css`: stat cards, library tiles, chip badges, hover overlays on book covers, format badges, progress bars baked onto the cover, slim scrollbars, empty-state pattern. All themed via Bootstrap CSS variables — works with every Bootswatch theme.
- ✅ **Home dashboard** — `IDashboardService` aggregates server stats (libraries / books / authors / finished count), "Continue Reading" carousel (per-user, ordered by `LastReadAt`), and "Recently Added" carousel.
- ✅ **EPUB reader** — `Reader.razor` at `/read/{id}` renders an `epub.js` viewer (loaded lazily from CDN) inside `#epub-viewer`. Live-tracks position via the `relocated` event, debounces, and pushes percentage + CFI back through a `DotNetObjectReference` to `IBookService.SaveProgressAsync`. Resumes from saved CFI on next open. JS interop module: `wwwroot/js/reader.js`.
- ✅ **PDF reader** — Same `/read/{id}` page falls back to a native browser `<iframe>` against `/files/{id}` for PDFs (Chromium / Firefox / Safari all ship a native viewer). Manual "Mark as read" button persists 100% completion. Page-level auto-progress is a follow-up using `pdf.js` directly.
- ✅ **Book file streaming** — `BookFilesController` at `/files/{bookId}` streams the raw EPUB / PDF bytes, range-enabled so big files don't get buffered. Auth-required so the URL isn't a public download link.
- ✅ **Reading progress hookup** — `IBookService.GetProgressAsync` exposes the calling user's saved location for a book; the reader uses it to resume; the dashboard's "Continue Reading" surfaces partially-read books.
- ✅ **Self-hosted reader libraries** — `epub.js` 0.3.93 + `jszip` 3.10.1 are now downloaded into `wwwroot/lib/{epubjs,jszip}/dist/` (tracked by `libman.json` so future restores stay deterministic). cdnjs only stocks epub.js up to 0.2.15 which lacks the modern Rendition API; `reader.js` therefore loads from local URLs.
- ✅ **Book metadata editor** — `/books/{id}/edit` (`Components/Pages/BookEdit.razor`) edits every UI-relevant field: title, sort title, subtitle, description (HTML), language, publisher, ISBN, published-on, series + number-in-series, authors, genres, tags. Authors / genres use the new generic `Components/Shared/ChipInput.razor` (TItem-typed pick-or-create chip widget); series uses an inline create-or-pick autocomplete; tags use Enter-to-add chip input. New entries are resolved via `*Service.GetOrCreateAsync` before `BookService.UpdateAsync` is called. "Edit" buttons surface on `BookDetail.razor` and the `BookCard.razor` hover overlay.
- ✅ **Bookmarks** — `IBookmarkService` (`ListAsync` / `CreateAsync` / `UpdateAsync` / `DeleteAsync`) is user-scoped (`IUserContextService` enforced server-side). Reader page exposes `<i class="bi-bookmark-plus">` to drop a bookmark at the current EPUB CFI or PDF page (page is prompted for in the absence of pdf.js), and `<i class="bi-bookmarks">` toggles a side drawer that lets you click to jump (`shelfwardenReader.gotoEpub` / `gotoPdfPage`) or delete entries. `BookDetail.razor` lists the user's bookmarks too (read-only / removable, no jump-from-detail without launching the reader).
- ✅ **Collections + Reading Lists UI** — `ICollectionService` and `IReadingListService` (Ardalis.Result-flavoured) sit on top of the existing `Collection` / `CollectionBook` / `ReadingList` / `ReadingListItem` entities. Collections come in two flavours: **personal** (OwnerUserId == calling user) and **global** (OwnerUserId == `Constants.GlobalUserId == "_global"`); only administrators can create / edit globals, but everyone sees them in the list. Reading lists are always personal. UI: `/collections` + `/collections/{id}` for browsing + add/remove books, `/reading-lists` + `/reading-lists/{id}` with up/down reorder buttons (the page sends the *full new ordering* to `IReadingListService.ReorderAsync` which compacts positions to `0..n-1`). `BookDetail.razor` has an "Add to…" dropdown that lazy-loads collections + lists on first open and supports inline create-and-add for both. Sidebar surfaces "Collections" / "Reading lists" with counts + a "Reading next" quick-list of the user's lists. Shared `BookProjections` helper centralises the `Book → BookListItemDto` projection so the new services emit the same shape as `BookService`.
- ✅ **First-run wizard** (`/setup`) — `ISetupService` + `SetupController` (MVC, sits alongside the Blazor app to be able to issue the auth cookie) drive a 3-step wizard rendered by `Components/Pages/Setup.razor` under a minimal `SetupLayout.razor` (no sidebar / topbar, since nothing's configured yet). Steps: **welcome** → **admin** (Identity only — creates / updates the seed admin and signs them in via `SignInManager`; Keycloak / None mode skip straight past this step) → **library** (creates the first `Library` + `LibraryFolder` and immediately enqueues a scan) → **done** (writes `setup.complete=true` to `IServerSettingsService` and lands the user on the dashboard). `SetupRedirectMiddleware` (registered in `Program.cs` after auth/authz, before routing) bounces every non-whitelisted request to `/setup` while `setup.complete` is false; the whitelist covers `/setup`, the `SetupController` POST endpoints, Identity pages, static assets and `/_blazor`. `SetupService` is allowed to run *before* an admin exists (it explicitly bypasses the normal `IUserContextService.IsAdministrator()` check) but only until `setup.complete` flips — afterwards every endpoint refuses. Errors flow back from `SetupController` via query-string (`?step=admin&error=...`) since `TempData` isn't available to Blazor components, and `Setup.razor` reads them with `[SupplyParameterFromQuery]`. `ServerSettingsService.InvalidateCache()` is poked when setup completes so the middleware doesn't keep redirecting from the cached `false`.
- ✅ **Self-hosted pdf.js** — `pdfjs-dist@3.11.174` (`build/pdf.min.js` + `build/pdf.worker.min.js`) is served from `wwwroot/lib/pdfjs/` (tracked in `libman.json`). `Reader.razor` renders PDFs into a `<div id="pdf-viewer">` instead of the previous `<iframe>`; `wwwroot/js/reader.js` mounts the document via `pdfjsLib.getDocument`, builds one `<canvas>` placeholder per page, and uses `IntersectionObserver` to lazily render pages as they scroll into view. The currently-visible page drives an automatic progress push (page number + percentage) back through `DotNetObjectReference.OnProgress`, so manual "Mark as read" is gone. New JS-side helpers `nextPdfPage` / `prevPdfPage` / `getPdfPage` / `gotoPdfPage` / `disposePdf` give Blazor proper navigation + bookmark capture (PDF bookmarks now grab the current page automatically — no more `prompt()` fallback). Reader keyboard shortcuts (`←` / `→`) work for both EPUB and PDF.
- ✅ **Provider migrations generated** — `InitialCreate` migrations now exist for `Shelfwarden.Data.Sql` (SQL Server) and `Shelfwarden.Data.Npgsql` (PostgreSQL) alongside the existing SQLite snapshot. **MySQL is intentionally skipped**: Pomelo doesn't yet ship an EF Core 10-compatible build, so `Shelfwarden.Data.MySql` only carries the project skeleton + DI extensions and will need migrations + provider verification once Pomelo catches up. To add migrations for a provider: set `Database:Provider` to that provider in `appsettings.Development.json` first so the `--startup-project` resolves the right factory, then run `dotnet ef migrations add <Name> -p Shelfwarden.Data.<Provider> -s Shelfwarden -c Shelfwarden.Data.<Provider>.ApplicationDbContext --output-dir Migrations`. EF 10.0.6+ tooling needs `Microsoft.EntityFrameworkCore.Design` on the **startup project** (`Shelfwarden`) too, otherwise `dotnet ef` fails with `MissingMethodException: AbstractionsStrings.ArgumentIsEmpty(Object)` (see [efcore#38107](https://github.com/dotnet/efcore/issues/38107)).

Next:
1. **Integration tests** for the auth-mode switch, scanner, reader, setup wizard.
2. **Recurring scans** — schedule each library's scan via Hangfire `RecurringJob` instead of (or in addition to) on-demand triggering.
3. **MySQL provider** — wait for Pomelo's EF Core 10 build, then wire `Shelfwarden.Data.MySql` migrations and re-enable the option in the wizard.

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

- The Bootswatch theme is loaded from `wwwroot/lib/bootswatch/dist/<theme>/bootstrap.min.css`. Default is `flatly`. The `<link id="theme-stylesheet">` in `Components/App.razor` is server-rendered with the active theme, and `wwwroot/js/shelfwarden.js` provides `setThemeHref` so admins can swap themes via the topbar's `ThemePicker.razor` without a full reload.
- Avoid baking colors into custom CSS. Use Bootstrap utility classes (`text-primary`, `bg-body-tertiary`, `border-subtle`, etc.) and CSS variables (`--bs-primary`, `--bs-emphasis-color`, `--bs-tertiary-bg`, etc.) so theme swaps propagate.
- The app shell is in `Components/Layout/MainLayout.razor` → `Sidebar.razor` (left) + `Topbar.razor` (top) inside an `.app-shell` flex container. `wwwroot/css/site.css` defines all the layout / card / chip / reader styles. Mobile collapses the sidebar via the `topbar-toggle` button.
- The reader (`/read/{id}`) loads `epub.js` + `jszip` lazily from `wwwroot/lib/{epubjs,jszip}/dist/` (self-hosted; tracked in `libman.json`) and renders into `#epub-viewer`. PDFs render via `pdf.js` (self-hosted at `wwwroot/lib/pdfjs/`) into a canvas-per-page layout under `#pdf-viewer`, with `IntersectionObserver` driving lazy rendering and current-page detection. File bytes stream through `BookFilesController` (`/files/{id}`) with `enableRangeProcessing: true`. Progress flows: JS event (`relocated` for EPUB, scroll-observer for PDF) → `DotNetObjectReference.OnProgress(percent, page, cfi)` → `IBookService.SaveProgressAsync` → `BookProgress` table. Bookmarks flow through `IBookmarkService` and reuse the same CFI / page coordinate space (`shelfwardenReader.getEpubLocation` / `gotoEpub` / `getPdfPage` / `gotoPdfPage`).
- The first-run wizard at `/setup` is the only flow that may run before there's an admin user. `SetupRedirectMiddleware` (`Shelfwarden/Infrastructure/SetupRedirectMiddleware.cs`) checks `IServerSettingsService.GetAsync("setup.complete")` on every request and bounces non-whitelisted requests there. The wizard itself is a Blazor page (`Components/Pages/Setup.razor`) under a minimal `Components/Layout/SetupLayout.razor`, and it posts to `Controllers/SetupController.cs` for anything that needs a real HTTP response (e.g. issuing the Identity auth cookie via `SignInManager`). `ISetupService` is the only service allowed to do admin-y work pre-setup; it explicitly bypasses `IUserContextService.IsAdministrator()` but flatly refuses once `setup.complete` is true, and calls `ServerSettingsService.InvalidateCache()` when flipping the flag so the middleware sees the change immediately. Don't add new pre-auth endpoints without adding their paths to the middleware's whitelist.
- Multi-select editor inputs (e.g. authors, genres on `BookEdit.razor`) use `Components/Shared/ChipInput.razor` — generic `TItem` with `NameOf`, `SearchAsync`, and `OnAddAsync` parameters. Parent owns the resolution (call `*Service.GetOrCreateAsync` from `OnAddAsync`) so the chip widget is dumb about entity types. Pattern: keep the selected list as a `List<TItem>` of the actual DTOs so save can grab `.Id` directly.
- Collections vs Reading Lists: both reuse `BookListItemDto` for the embedded books via the shared `BookProjections` helper (`ToListItem` + `LoadProgressPercentagesAsync`). Auth model: `ICollectionService` is the only place that distinguishes personal vs global — `Constants.GlobalUserId` is the magic owner string. `IReadingListService` is simpler (always per-user). Both services are idempotent on `AddBookAsync` (re-adding a book returns Success rather than Conflict) so naive UIs that double-click can't break things. Reading list reorder is "send the full new ordering" — the service compacts positions to a contiguous `0..n-1` so up/down arrows stay symmetrical and a missing id silently drops out.
- Identity Razor pages live under `Areas/Identity/Pages/` with custom `_Layout.cshtml` so we don't depend on the embedded `_LoginPartial` (which Blazor Server doesn't ship). The custom layout reads the active theme via `IServerSettingsService`.
- Keep services provider-agnostic. They depend on `IRepository<T>`, never on `ApplicationDbContext` directly.
- New entities: add the POCO + `IEntityTypeConfiguration` to `Shelfwarden.Data\Entities\<Name>.cs`, add a `DbSet<>` to `ApplicationDbContextBase`, run `dotnet ef migrations add <Name> --project Shelfwarden.Data.Sqlite --startup-project Shelfwarden`.
- When generating migrations for the non-Sqlite providers later, set `Database:Provider` to that provider in `appsettings.Development.json` first so the `--startup-project` resolves the right factory.
