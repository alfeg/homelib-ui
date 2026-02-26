# Copilot Instructions for MyHomeLib

## Project Overview

**MyHomeLib** is a self-hosted web application for searching and downloading books from the [Flibusta](https://flibusta.is/) e-book library, which is distributed as a BitTorrent (~600 GB, ~545 000 books). It consists of:

- **`MyHomeLib.Web`** — ASP.NET Core 10 Minimal API backend. Serves the Vue SPA, streams the INPX index to the client, and proxies book file downloads directly to the browser via TorrServe.
- **`MyHomeLib.Ui`** — Vue 3 + TypeScript SPA. Parses the INPX in a Web Worker, builds a MiniSearch full-text index stored in IndexedDB, and handles all search/filter interactions client-side.
- **TorrServe** — separate sidecar process. Handles all BitTorrent work. MyHomeLib communicates with it over HTTP.

The key design principle: **no full torrent download**. Only the INPX index (~35 MB) is fetched for indexing; individual books are streamed on demand via HTTP Range requests into ~2–2.5 GB ZIP archives.

## Solution Structure

```
MyHomeLibServer.slnx
├── MyHomeLib.Library/   # Book data model + INPX parser (.NET, Library)
├── MyHomeLib.Torrent/   # TorrServe client + download manager (.NET, Library)
├── MyHomeLib.Web/       # ASP.NET Core backend (.NET, Exe, net10.0)
└── MyHomeLib.Ui/        # Vue 3 SPA (TypeScript + Vite + Tailwind v4 + DaisyUI)
```

### Project Dependencies

- `MyHomeLib.Web` → `MyHomeLib.Torrent` → `MyHomeLib.Library`
- `MyHomeLib.Ui` is a standalone frontend; `MyHomeLib.Web` serves its `dist/` output

## Key Technologies

### Backend (.NET)

| Concern            | Library                                                     |
| ------------------ | ----------------------------------------------------------- |
| Web framework      | ASP.NET Core 10 Minimal API (`Microsoft.NET.Sdk.Web`)       |
| Torrent streaming  | TorrServe HTTP API (`TorrServeClient` — plain `HttpClient`) |
| Book index storage | DuckDB file-backed database (`DuckDB.NET.Data.Full` v1.4.4) |
| INPX parsing       | `CsvHelper` (v33.0.1) + `SharpZipLib` (v1.4.2)              |
| FB2 book parsing   | `Fb2.Document` (v2.4.0)                                     |
| DI / configuration | `Microsoft.Extensions.*` (implicit via Web SDK)             |

### Frontend (Node / Browser)

| Concern     | Library                                          |
| ----------- | ------------------------------------------------ |
| Framework   | Vue 3 (Composition API, `<script setup>`)        |
| Language    | TypeScript                                       |
| Build       | Vite                                             |
| CSS         | Tailwind CSS v4 + DaisyUI                        |
| Search      | MiniSearch (BM25, AND, prefix) in a Web Worker   |
| ZIP parsing | fflate (streaming)                               |
| State       | VueUse `createGlobalState`                       |
| i18n        | Custom `i18n.ts` (ru / en message maps)          |
| Persistence | IndexedDB (parsed INPX cache + MiniSearch index) |

## Architecture & Data Flow

### 1. Backend — INPX delivery

`LibraryBooksCacheService` fetches the INPX file on demand from TorrServe via `DownloadManager`. No disk caching — every `/api/library/inpx` request streams directly from TorrServe.

`Program.cs` Minimal API endpoints:

- `GET /api/library/inpx` — streams the INPX archive to the browser.
- `GET /api/library/status` — returns current indexing / torrent status as JSON.
- `POST /api/library/download` — streams the requested book directly to the browser via TorrServe (nothing saved on server).

### 2. Frontend — parsing and search (Web Worker)

`searchIndex.worker.ts` (Web Worker):

- Receives the INPX stream from the server, parses it with `inpxParser.ts` (fflate streaming unzip + pipe-delimited row parsing).
- Builds a MiniSearch index over `title`, `authors`, `series`, `genre`, `lang` fields.
- Persists the parsed books and the MiniSearch index in IndexedDB (keyed by `LIBRARY_CACHE_DB_VERSION`).
- On subsequent loads, restores index directly from IndexedDB (bypassing server fetch and parsing).
- Handles `search` messages: text match → year filter → genre facets → genre filter → pagination.
- Returns `SearchResult` with `books`, `totalBooks`, `genreCounts`, `yearFrom`, `yearTo`.

`searchIndexWorkerClient.ts` — thin typed wrapper that sends messages to the worker and resolves Promises.

### 3. Global state

`useLibraryState.ts` — `createGlobalState` composable exposing:

- `searchTerm`, `selectedGenres`, `selectedYearFrom`, `selectedYearTo`, `availableYearRange`
- `results`, `currentPage`, `isLoading`, `isIndexing`, `indexingProgress`
- `search()`, `clearFilters()`

### 4. Download flow

User clicks download → `POST /api/library/download` → `DownloadManager.DownloadFile()`:

- Asks TorrServe for the file list of the torrent.
- Finds the right ZIP archive by filename (`ArchiveFile` from INPX).
- Opens `HttpRangeStream` against the TorrServe streaming URL.
- Uses `ZipArchive` to read only the specific book entry via HTTP Range, without downloading the full archive.
- Returns `Results.File(bytes, contentType, fileName)` — the file is streamed directly to the browser. Nothing is written to disk on the server.

### 5. INPX field layout

Each `.inp` row is pipe-delimited (`|`):

```
authors | genre | title | series | seriesNo | file | size | id | deleted | ext | date | lang
  [0]     [1]    [2]     [3]       [4]        [5]   [6]   [7]    [8]      [9]  [10]  [11]
```

Authors inside a field are `:` separated; each author is `LastName,FirstName,MiddleName`.

## Core Classes — Backend

| Class             | Project | Role                                                                                                                               |
| ----------------- | ------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| `LibraryService`  | Web     | BackgroundService; downloads INPX and caches it to disk (`app_data/library_cache/`), exposes `IndexTask`                           |
| `LibraryConfig`   | Web     | Strongly-typed config POCO for `Library:*` settings                                                                                |
| `DownloadManager` | Torrent | Orchestrates book download via TorrServe                                                                                           |
| `TorrServeClient` | Torrent | HTTP wrapper for TorrServe API (`/torrents`, `/stream`, `/echo`)                                                                   |
| `HttpRangeStream` | Torrent | Seekable `Stream` backed by HTTP Range requests                                                                                    |
| `MagnetUriHelper` | Torrent | Parses info-hash hex from a magnet URI (regex)                                                                                     |
| `AppConfig`       | Torrent | Strongly-typed config POCO for `Torrent:*` settings                                                                                |
| `InpxReader`      | Library | Async streaming INPX parser; yields `BookItem` via `IAsyncEnumerable`                                                              |
| `BookItem`        | Library | Book metadata record: `Id`, `Authors`, `Title`, `Genre`, `Series`, `Lang`, `Ext`, `ArchiveFile`, `File`, `Size`, `Date`, `Deleted` |

## Core Files — Frontend (`MyHomeLib.Ui/src/`)

| File                                  | Role                                                     |
| ------------------------------------- | -------------------------------------------------------- |
| `workers/searchIndex.worker.ts`       | MiniSearch Web Worker — parse, index, search, persist    |
| `workers/inpxParser.ts`               | INPX archive parser (fflate + streaming)                 |
| `services/searchIndexWorkerClient.ts` | Typed message bridge to the worker                       |
| `services/i18n.ts`                    | `ru`/`en` message maps; `useI18n()` composable           |
| `composables/useLibraryState.ts`      | Global reactive state via VueUse `createGlobalState`     |
| `types/library.ts`                    | `BookRecord`, `SearchQuery`, `SearchResult` shared types |
| `App.vue`                             | Root component                                           |
| `components/LibraryControls.vue`      | Search bar + toolbar                                     |
| `components/GenreSidebar.vue`         | Left sidebar: genre list + year range filter             |
| `components/GenreList.vue`            | Scrollable genre checkboxes                              |
| `components/YearRangeFilter.vue`      | Year-from / year-to inputs                               |
| `components/BooksTable.vue`           | Results table with download button                       |
| `components/TablePagination.vue`      | Page controls                                            |
| `components/MagnetGate.vue`           | Landing screen when no library is configured             |
| `components/SearchBar.vue`            | Search input with debounce                               |

## TorrServe API Notes

- `POST /torrents` with `{"action":"get","hash":"<hex>"}` returns a `TorrServeTorrent` JSON object.
  - `stat` int: 2 = preload, 3 = working, 4 = closed, 5 = in DB.
  - `data` field is a JSON-encoded **string** containing `{"TorrServer":{"Files":[...],"TorrentStats":{...}}}`.
  - `TorrentStats` inside `data.TorrServer`: `download_speed`, `upload_speed`, `total_peers`, `active_peers`, `connected_seeders`, `loaded_size`, `torrent_size`.
- `GET /echo` returns a plain-text version string — used for health checks.
- `GET /stream?link=<hash>&index=<fileId>&play` streams the file via HTTP Range requests.
- Use `http://127.0.0.1:8090` (not `localhost`) to avoid IPv6 resolution issues on Windows.
- `TorrServeClient` handles both v1 (`file_stat`) and v2+ (`data.TorrServer.Files`) response formats.

## Configuration

All settings are bound via `IConfiguration` (`appsettings.json` + environment variables with `__` separator).

### `Library` section → `LibraryConfig`

| Key         | Description                                    |
| ----------- | ---------------------------------------------- |
| `MagnetUri` | Magnet URI of the library torrent              |
| `InpxPath`  | Optional path to a pre-downloaded `.inpx` file |

### `Torrent` section → `AppConfig`

| Key            | Description                                                          |
| -------------- | -------------------------------------------------------------------- |
| `TorrServeUrl` | Base URL of the TorrServe instance (default `http://127.0.0.1:8090`) |

## Data Layout

No database files. No book files saved on the server — downloads stream directly to the browser. No disk caching — INPX is fetched on demand from TorrServe.

## Docker / Deployment

Run with `docker compose up` from the repo root. The `docker-compose.yml` starts:

- `yourok/torrserver:latest` on port `8090`
- `myhomelib` (built from `Dockerfile`) on port `8080`

The `Dockerfile` is a multi-stage build: .NET 10 SDK + Node.js 20 build stage → ASP.NET 10 runtime image.

Volumes: `torrserve_data` for TorrServe state.

Use `docker-compose.prod.yml` for production (TorrServe port not exposed).

Minimum VPS requirements: ~512 MB RAM server-side. Browser requires ~300–500 MB RAM for the MiniSearch index.

## Coding Conventions

### Backend (.NET)

- **Nullable reference types** enabled — always annotate nullability.
- **Implicit usings** enabled — avoid redundant `using` directives.
- Solution file uses the modern **SLNX** format (`MyHomeLibServer.slnx`). Do not regenerate a legacy `.sln` file.
- DI is constructor-injected. Services used both as singletons and hosted services are registered with both `AddSingleton` and `AddHostedService`.
- Prefer `IAsyncEnumerable` for streaming data (INPX parsing, search results).
- `HttpClient` is registered as `new HttpClient()` directly (not via `IHttpClientFactory`) to avoid socket error 10049 (WSAEADDRNOTAVAIL) on Windows.
- No MonoTorrent, no Parquet.Net, no Spectre.Console — all removed.

### Frontend (Vue / TypeScript)

- Vue 3 Composition API with `<script setup lang="ts">` in all components.
- All props and emits must be typed; avoid `any`.
- Tailwind v4 utility classes (no `tailwind.config.js` class generation quirks — use standard v4 syntax).
- i18n: use `useI18n()` from `i18n.ts`; add keys to both `ru` and `en` message maps simultaneously.
- State mutations only through composable functions in `useLibraryState.ts`.
- Worker communication only through `searchIndexWorkerClient.ts` — do not post messages directly.
- `LIBRARY_CACHE_DB_VERSION` in `searchIndex.worker.ts` must be incremented when `BookRecord` schema changes.
- `PERSISTENCE_DB_VERSION` must be incremented when the IndexedDB persistence schema changes.
