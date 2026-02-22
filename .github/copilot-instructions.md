# Copilot Instructions for MyHomeLib

## Project Overview

**MyHomeLib** is a .NET 10 Blazor Server web application for searching and downloading books from the [Flibusta](https://flibusta.is/) e-book library. Libraries are distributed as magnet-linked torrents containing an INPX index file and ZIP book archives. Torrent streaming is delegated entirely to **TorrServe** — a separate sidecar process that must be running alongside the app.

## Solution Structure

```
MyHomeLibServer.slnx
├── MyHomeLib.Web/      # Blazor Server web app (Exe, net10.0, Microsoft.NET.Sdk.Web)
├── MyHomeLib.Torrent/  # TorrServe client + download manager (Library, net10.0)
└── MyHomeLib.Library/  # Book data models + INPX parser (Library, net10.0)
```

### Project Dependencies
- `MyHomeLib.Web` → `MyHomeLib.Torrent` → `MyHomeLib.Library`

## Key Technologies

| Concern | Library |
|---|---|
| Web framework | ASP.NET Core Blazor Server (`Microsoft.NET.Sdk.Web`) |
| Torrent streaming | TorrServe HTTP API (`TorrServeClient` — plain `HttpClient`) |
| Book index storage | DuckDB file-backed database (`DuckDB.NET.Data.Full` v1.4.4) |
| Book index parsing | `CsvHelper` (v33.0.1) + `SharpZipLib` (v1.4.2) |
| FB2 book parsing | `Fb2.Document` (v2.4.0) |
| Human-readable output | `Humanizer.Core` (v3.0.1) |
| DI / configuration | `Microsoft.Extensions.*` (implicit via Web SDK) |

## Architecture & Data Flow

1. **Startup — Library Indexing** (`LibraryService` BackgroundService):
   - On first run, registers the magnet URI with TorrServe → polls until the `.inpx` file appears → streams it through `InpxReader` → inserts all `BookItem` rows into a file-backed DuckDB (`.db` file alongside the downloads directory).
   - On restart, if the DuckDB already contains rows, parsing is skipped entirely and the FTS index is rebuilt from existing data.

2. **Search** (`BookSearchIndex`):
   - Queries the DuckDB file using a full-text search (FTS) PRAGMA over title/author/series/genre/language columns.
   - Returns reconstructed `BookItem` records. No in-memory list is kept.

3. **Download** (`DownloadManager` + `DownloadQueueService`):
   - User enqueues a book → `DownloadQueueService` (BackgroundService) persists the job to a DuckDB queue table → calls `DownloadManager.DownloadFile()`.
   - `DownloadManager` asks TorrServe for the file list, finds the right ZIP archive, opens an `HttpRangeStream` (seekable HTTP Range request stream) against the TorrServe streaming URL, unzips the specific book entry, and writes it to the downloads directory.

4. **Status** (`TorrentStatusPanel`):
   - Polls `DownloadQueueService.LibraryStats` every 3 s.
   - Shows TorrServe connectivity, torrent state, ↓/↑ transfer speeds, peer/seeder counts, and a progress bar of cached bytes vs total torrent size.

### INPX Format
An INPX file is a ZIP archive containing `.inp` files (pipe-delimited CSV rows) with book metadata, plus `collection.info` and `version.info` metadata files.

### Data Layout (configured via `Library:*` config keys)
```
<DownloadsDirectory>/
  queue.db          # Download job queue (DuckDB)
  books.db          # Book index (DuckDB, file-backed, reused across restarts)
  <book files>      # Downloaded books
```

## Configuration

All settings are bound via `IConfiguration` (`appsettings.json` + environment variables). Environment variables use double-underscore separators (`Library__MagnetUri`).

### `Library` section → `LibraryConfig`
| Key | Description |
|---|---|
| `MagnetUri` | Magnet URI of the Flibusta INPX library torrent |
| `DownloadsDirectory` | Where downloaded books and the queue DB are stored |
| `LibraryDbPath` | Override path for the DuckDB book index file |
| `QueueDbPath` | Override path for the DuckDB download queue file |
| `TorrentEnabled` | Computed: true when MagnetUri and DownloadsDirectory are set |

### `Torrent` section → `AppConfig`
| Key | Description |
|---|---|
| `TorrServeUrl` | Base URL of the TorrServe instance (default `http://127.0.0.1:8090`) |

## Core Classes

- **`LibraryService`** (`MyHomeLib.Web`) — `BackgroundService`; on startup calls `DownloadManager.StartLibraryAsync()` then `BookSearchIndex.BuildAsync()`; exposes `IndexTask` (`TaskCompletionSource`) so UI can await indexing completion.
- **`BookSearchIndex`** (`MyHomeLib.Web`) — file-backed DuckDB wrapper; `BuildAsync(IAsyncEnumerable<BookItem>)` streams inserts; `SearchAsync()` queries via FTS; skips rebuild if rows already exist.
- **`DownloadQueueService`** (`MyHomeLib.Web`) — `BackgroundService`; persists download jobs to DuckDB; processes them sequentially; samples `RefreshStatsAsync` every 3 s for the status panel.
- **`DownloadManager`** (`MyHomeLib.Torrent`) — TorrServe-only download orchestration; `StartLibraryAsync()`, `DownloadFile()`, `RefreshStatsAsync()`, `GetStats()`.
- **`TorrServeClient`** (`MyHomeLib.Torrent`) — thin HTTP wrapper for the TorrServe API (`/torrents`, `/stream`, `/echo`); handles v1 (`file_stat`) and v2+ (`data.TorrServer.Files`) file-list formats.
- **`HttpRangeStream`** (`MyHomeLib.Torrent`) — seekable `Stream` backed by HTTP Range requests; issues a HEAD request on first `Length` access to get real `Content-Length` from TorrServe.
- **`InpxReader`** (`MyHomeLib.Library`) — async streaming parser for INPX archives; yields `BookItem` via `IAsyncEnumerable`.
- **`BookItem`** (`MyHomeLib.Library`) — book metadata record: `Id`, `Authors`, `Title`, `Genre`, `Series`, `Lang`, `Ext`, `ArchiveFile`, `File`, `Size`, `Date`, `Deleted`, etc.
- **`AppConfig`** (`MyHomeLib.Torrent`) — strongly-typed config POCO for `Torrent:*` settings.
- **`MagnetUriHelper`** (`MyHomeLib.Torrent`) — parses the info-hash hex string from a magnet URI via regex.

## TorrServe API Notes

- `POST /torrents` with `{"action":"get","hash":"<hex>"}` returns a `TorrServeTorrent` JSON object.
  - `stat` int: 2 = preload, 3 = working, 4 = closed, 5 = in DB.
  - `data` field is a JSON-encoded **string** containing `{"TorrServer":{"Files":[...],"TorrentStats":{...}}}`.
  - `TorrentStats` inside `data.TorrServer` has: `download_speed`, `upload_speed`, `total_peers`, `active_peers`, `connected_seeders`, `loaded_size`, `torrent_size`.
- `GET /echo` returns a plain-text version string — used for health checks.
- `GET /stream?link=<hash>&index=<fileId>&play` streams the file via HTTP Range requests.
- Use `http://127.0.0.1:8090` (not `localhost`) to avoid IPv6 resolution issues on Windows.

## Docker / Deployment

Run with `docker compose up` from the repo root. The `docker-compose.yml` starts:
- `yourok/torrserver:latest` on port `8090`
- `myhomelib` (built from `Dockerfile`) on port `8080`

Volumes: `torrserve_data` for TorrServe state, `books_data` mounted at `/data/books` for downloads and the DuckDB files.

Minimum VPS requirements: ~512 MB RAM (no large in-memory book list), adequate disk for the downloads directory.

## Coding Conventions

- **Nullable reference types** enabled — always annotate nullability.
- **Implicit usings** enabled — avoid redundant `using` directives.
- Solution file uses the modern **SLNX** format (`MyHomeLibServer.slnx`). Do not regenerate a legacy `.sln` file.
- DI is constructor-injected. `LibraryService` is registered as both `AddSingleton` and `AddHostedService` so it can be injected and lifecycle-managed.
- Prefer `IAsyncEnumerable` for streaming data (INPX parsing, search results).
- No MonoTorrent, no Parquet.Net, no Spectre.Console, no CLI commands — all removed.
- `HttpClient` is registered as `new HttpClient()` directly (not via `IHttpClientFactory`) to avoid socket error 10049 (WSAEADDRNOTAVAIL) on Windows.

