# Shelfwarden

Self-hosted ebook library manager — a Kavita-style server, but for ebooks only, with a flexible file layout, multiple database providers, and three auth modes (ASP.NET Identity, Keycloak, or None).

> See [CONTEXT.md](./CONTEXT.md) for the full architecture, conventions and roadmap. Cursor / AI assistants should read it first.

## Features

### Shelves & scanning

- **Shelves** are your library roots. Each shelf has one or more folders on disk, browsed with a built-in filesystem picker, and books can live at any depth beneath them — no naming convention required.
- **EPUB and PDF** are scanned for title, subtitle, description, language, publisher, ISBN, published date, page count, authors, series + number in series, genres, tags and the embedded cover.
- **Calibre libraries** are supported as a folder-structure option: the sibling `metadata.opf` wins over the file's own metadata, and Calibre tags come across as book tags.
- **Change detection** by path, last-write time and size, so re-scans only touch what actually changed. Books whose files have disappeared are removed.
- **Per-shelf import options:** always use the file name as the title, always ignore author / tag / genre metadata, file newly-imported books into a collection (default "To Review"), and auto-fetch missing metadata online.
- **`shelfwarden_import.json` sidecars** can merge or replace authors, genres and tags for everything beneath a folder.
- Scans run on Hangfire's dedicated `scan` queue with a live per-shelf status badge.

### Books & metadata

- Full metadata editor: title, sort title, subtitle, HTML description, language, publisher, ISBN, published date, series and number in series, authors, genres, tags, universe and its timeline date (picked from the universe's dates, with a shortcut for adding a new one).
- **Online metadata lookup** across four providers in parallel — Google Books, Open Library, Amazon and Goodreads — ranked by ISBN match, title similarity, field completeness and provider priority. Use it interactively from the edit page, or let a shelf back-fill empty fields during import.
- **Cover management:** pick a cover from an online candidate, or revert to the file's embedded cover.
- **Download** the original EPUB or PDF from the book detail page.
- **Batch edit** up to 100 books at once — set series, publisher and language across the selection, add / replace / remove authors, genres and tags, and edit titles, subtitles and series numbers row by row.
- **Bulk actions** from the books grid: add to a collection or reading list, or jump into batch edit.

### Browsing, search & filtering

- **Topbar search** for books, authors and series from anywhere in the app.
- Grid or list view with infinite scroll, plus an A–Z letter rail. Cover tiles show format badges and your reading progress.
- Filter by shelf, collection, author, series, genre, tag (with "None" options), read status, your own star rating, and "awaiting review" for books never edited since import.
- Sort by title, date added, date updated or published date, ascending or descending.
- **Dashboard** with server stats, a "Continue Reading" carousel of your in-progress books and a "Recently Added" carousel.

### Authors, series, collections & reading lists

- **Authors** get a detail page with photo, biography, external links, pseudonyms and their books grouped by series. Biographies and photos can be imported from Open Library, Wikidata or Goodreads. Admins can merge or delete authors in bulk, and a weekly job cleans up authors left with no books.
- **Series** pages list books in reading order and roll up any related extra content.
- **Collections** are arbitrary themed groupings, either personal or global (admin-created, visible to everyone).
- **Reading lists** are per-user ordered queues with drag-and-drop reordering, move-to-top / move-to-bottom shortcuts, a "Read next" button, and the ability to add a whole series at once. The sidebar also surfaces a "Reading next" quick list.
- **Add to…** is a single dialog available from every book and series tile and detail page, covering collections, reading lists and universes, with create-and-add for a new one.

### Universes

- Group the series and books that share a fictional world, with tabs for **Overview**, **Series**, **Books**, **Reading orders** and **Timeline**.
- Each universe is either **Named** (dates are free text like "10,191 AG", "Spring 1998", "Before the Fall", arranged by hand) or **Numeric** (dates carry a year, or year range, instead) — picked with a couple of radio buttons the first time you add a date, and switchable later the same way. Books are assigned to a date from a list rather than typed against one, so several books can share a moment, and books with no date yet wait in an **Unscheduled** slot.
- The timeline reads as one lane per series (standalone books share a lane). **Named** universes get an evenly-spaced column per date; **Numeric** ones get a proportional axis with tick marks, so two different dates with overlapping ranges visibly overlap across lanes — and overlapping (or touching) dates *on the same lane* merge into a single labelled bar spanning their combined years, rather than showing as separate overlapping bars. Book covers never clutter the chart itself: click a lane's own name to jump to its series page (or, for the shared "Standalone" lane, open a covers modal), or click one of its bars to open a covers modal for just the books on it. **Edit timeline** switches to a per-date editor — up/down arrows move dates along the timeline, drag-and-drop reorders books within a date, and dates (Named text, or Numeric years) can be added, renamed or deleted from either view.
- **Reading orders** are ordinary reading lists scoped to the universe (publication order, chronological order, and so on). A new one starts as the whole universe in timeline order, ready to be rearranged. They're shared with every reader and stay out of the personal reading lists page.
- Series and book membership are tracked independently, so a book can sit in a universe whose series doesn't, and vice versa. Assigning a series to a universe can optionally add its current books to the timeline.

### Reading

- **EPUB reader** built on epub.js with zoom, keyboard navigation, reader dark mode and CFI-accurate resume.
- **PDF reader** built on pdf.js, rendering a canvas per page with lazy rendering as you scroll and automatic page-level progress.
- **Bookmarks** with optional notes, captured at the current EPUB location or PDF page, with jump-to from the reader drawer.
- Reading progress is per user; a book counts as finished at 99%.
- Both reader libraries are self-hosted — no CDN calls at runtime.

### Audiobooks (text to speech)

- Turn any book into an `.m4a` audiobook using KokoroSharp's neural TTS and FFmpeg for AAC encoding, on a dedicated Hangfire queue.
- **Chapter detection** from EPUB navigation or PDF bookmarks / heading heuristics, with a section editor that auto-excludes front and back matter and can split the output into one file per chapter. Repeating PDF headers and footers are stripped before synthesis.
- **Voice picker** with per-voice audio previews, grouped by language.
- Stream the whole book, individual chapters, or download everything as a ZIP. Generation is resumable — a retry skips chunks and chapters already rendered.
- An **Audiobooks** admin page lists every generation across the library with status and progress.

### Extra content

- Register non-ebook files (artwork, notes, maps, companion PDFs) without moving them into your library.
- Images open in a lightbox, PDFs and text/HTML render in-app, everything else downloads.
- Assign items to an author, book or series, tag them, and rename them for display.

### Per-user state

- Star ratings (1–5) and free-form notes per book, reading progress, and bookmarks are all scoped to the signed-in user.

### Administration

- **User management** for Identity mode (create, edit, delete, assign roles, lock out). Keycloak users are listed read-only.
- **Shelf access control** per user and per role — an unrestricted shelf is visible to everyone signed in, and admins always see everything.
- **Metadata admin** for genres, book tags and extra-content tags: search, create, merge, delete, and remove unused entries.
- **Hangfire dashboard** at `/hangfire` and a **Sejil log viewer** at `/sejil`, both admin-only.
- **First-run wizard** at `/setup` that creates the admin account, adds your first shelf and kicks off the initial scan.

### Look & feel

- 26 Bootswatch themes, split into dark and light tabs in the theme picker, previewed live and saved per browser.
- Custom card banners for shelves, collections and reading lists — random covers, a hand-picked set of covers, or your own uploaded image.
- Responsive Kavita-style shell with a collapsible sidebar.

### Deployment

- **Multi-database:** SQLite (default), PostgreSQL, SQL Server, MySQL — each with its own EF Core migrations.
- **Three auth modes:** ASP.NET Identity, Keycloak (OIDC), or None (a synthetic local user, for desktop).
- **Docker Compose** setup with PostgreSQL and an optional Keycloak container.
- **ElectronNET desktop build** that forces SQLite + None auth so non-technical users can just install and run it.

## Installation

The recommended self-hosted setup is **Docker Compose + PostgreSQL**. Run Postgres (and optionally pgAdmin) as its own stack on a shared Docker network, then attach Shelfwarden to that network. The same Postgres instance can be reused by other stacks, which saves RAM and disk compared with a dedicated database container per app.

Volume paths below (`/volume1/docker/...`) are Synology-style examples — change them to wherever you keep Docker data.

### 1. Create the shared network

```bash
docker network create postgres-net
```

### 2. PostgreSQL (+ pgAdmin)

```yaml
version: "3.9"

services:
  db:
    image: postgres:18-alpine
    container_name: postgres
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: your db password
    volumes:
      - /volume1/docker/postgresql/data:/var/lib/postgresql:rw
    ports:
      - "7001:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 5s
      timeout: 5s
      retries: 5
    restart: unless-stopped
    networks:
      - postgres-net

  pgadmin:
    image: dpage/pgadmin4:latest
    container_name: pgadmin
    environment:
      PGADMIN_DEFAULT_EMAIL: you@example.com
      PGADMIN_DEFAULT_PASSWORD: your pgadmin password
    ports:
      - "7002:80"
    volumes:
      - /volume1/docker/postgresql/pgadmin:/var/lib/pgadmin:rw
      - /volume1/docker/postgresql/backups:/var/lib/pgadmin/storage/you_example.com/backups:rw
    restart: unless-stopped
    networks:
      - postgres-net

networks:
  postgres-net:
    external: true
```

Replace the passwords (and the pgAdmin email) before you bring the stack up.

The backups bind-mount uses `you_example.com` — that is the same address as `PGADMIN_DEFAULT_EMAIL`, with `@` replaced by `_`. If you sign in as `you@example.com`, pgAdmin stores user files under `you_example.com`.

Then create a `shelfwarden` database in pgAdmin (or `psql`). The official Postgres image only auto-creates a database matching `POSTGRES_USER` (`postgres`), not `shelfwarden`.

### 3. Shelfwarden

```yaml
version: "3.9"

services:
  app:
    image: ghcr.io/gordon-matt/shelfwarden:latest
    container_name: Shelfwarden
    ports:
      - "7008:8080"
    environment:
      ASPNETCORE_URLS: http://+:8080
      Database__Provider: Npgsql
      ConnectionStrings__DefaultConnection: "Host=db;Port=5432;Database=shelfwarden;Username=postgres;Password=<your db password>"
      Storage__CoversPath: /app/data/covers
      Storage__AudiobooksPath: /app/data/audiobooks
      Storage__AuthorPhotosPath: /app/data/author-photos
      Storage__CardBannersPath: /app/data/card-banners
      Storage__ExtrasPath: /app/data/extras
      Storage__TtsCachePath: /app/data/tts-cache
      BulkEditMaxBookCount: 100
    volumes:
      - /volume1/docker/shelfwarden/library:/app/data/library
      - /volume1/docker/shelfwarden/audiobooks:/app/data/audiobooks
      - /volume1/docker/shelfwarden/author-photos:/app/data/author-photos
      - /volume1/docker/shelfwarden/card-banners:/app/data/card-banners
      - /volume1/docker/shelfwarden/covers:/app/data/covers
      - /volume1/docker/shelfwarden/extras:/app/data/extras
      - /volume1/docker/shelfwarden/tts-cache:/app/data/tts-cache
      - /volume1/docker/shelfwarden/logs:/app/data/logs
    networks:
      - postgres-net
    restart: unless-stopped

networks:
  postgres-net:
    external: true
```

Use the same database password as in the Postgres stack. Shelfwarden reaches Postgres by the `db` hostname because both containers share `postgres-net`.

Open the app at `http://<host>:7008` and complete the first-run wizard. Point each shelf at a folder under `/app/data/library` (the bind-mounted library path). Covers, author photos, audiobooks and the Kokoro / FFmpeg cache persist on the host under the other `/volume1/docker/shelfwarden/...` mounts.

Any other stack can join `postgres-net` the same way and share this Postgres instance.

### Desktop (Windows)

For a single-user install without Docker, build the NSIS installer from a machine with the .NET 10 SDK and Node.js 22+:

```powershell
.\create-installer-win.ps1
```

The installer is written to `Shelfwarden.Desktop\publish\Release\net10.0\win-x64\Shelfwarden-Setup-1.0.0.exe`. The desktop build forces SQLite + no-login auth. Application data (database, covers, author photos, audiobooks, TTS cache) lives under `%LOCALAPPDATA%\Shelfwarden\desktop`, not next to the program files.

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

### Run with the in-repo Compose file (PostgreSQL + optional Keycloak)

For production, prefer the [recommended Installation](#installation) (shared `postgres-net`, published image, bind-mounted storage). For a quick local stack from this repo:

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