# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

All frontend commands run from `MyHomeLib.Ui/`:

```bash
npm run dev              # Vite dev server on :5175
npm run build            # → MyHomeLib.Ui/dist (SPA, single-file inlined)
npm run build-standalone # → docs/index.html (self-contained, GitHub Pages)
npm test                 # vitest run
npm run test:watch       # vitest watch
npm run ts               # vue-tsc --noEmit
npm run lint             # eslint --fix + prettier -w
```

Single test file / single test:

```bash
npx vitest run src/workers/__tests__/searchIndex.worker.test.ts
npx vitest run -t "restores index from cache"
```

The `inpx-parser` package is consumed via `file:../inpx-parser`, so **its `dist/` must be built before the UI can resolve it**:

```bash
cd inpx-parser && npm run build
```

Backend:

```bash
dotnet run --project MyHomeLib.Web    # http://localhost:5080, https://localhost:7080
dotnet build MyHomeLibServer.slnx
docker compose up -d                  # app on :5080, TorrServe on :8090
```

There is no .NET test project.

`dotnet publish -c Release` triggers the `BuildClientUi` MSBuild target: it runs `npm ci && npm run build` in `MyHomeLib.Ui/`, wipes `MyHomeLib.Web/wwwroot/`, and copies `dist/` into it. Pass `-p:BuildClientApp=false` to skip (the Dockerfile does this and copies the UI in from its own stage).

## Branch: `feature/opds-meilisearch`

On this branch the server gained a second, independent stack. **Both exist side by side** —
the SPA still does its own client-side INPX parsing and MiniSearch indexing (its migration
is deliberately not done yet), while these routes are served from a server-side index:

| Surface | Routes |
|---|---|
| Thin HTML (no JS, e-ink) | `/m`, `/m/search`, `/m/genres`, `/m/author`, `/m/series`, `/m/book/{uid}` |
| OPDS 1.2 catalog | `/opds`, `/opds/search`, `/opds/author`, `/opds/series`, `/opds/genre/{code}`, `/opds/opensearch.xml` |
| JSON API | `/api/search`, `/api/author`, `/api/series`, `/api/book/{uid}`, `/api/status`, `/api/genres` |
| Download | `GET /download/{uid}`, `GET /download/{uid}.zip` |

Key differences from master:

- **The magnet URI comes from `Library:MagnetUri` only.** Clients cannot supply one; the
  legacy `POST /api/library/*` endpoints ignore any magnet in the request body.
- **INPX is parsed server-side** by `MyHomeLib.Library` (revived, CsvHelper dropped) and bulk
  loaded into **Meilisearch** by `LibraryIndexerService` at startup. ~1.5s to parse 700k rows,
  ~5 min for the first Meilisearch load, ~10s on restart when nothing changed.
- Books are keyed by `uid` — a stable FNV-1a/64 of `archive|file|ext`. **Do not key on the
  INPX id**: a real collection carries ~57 duplicates.
- `Date`/`year` is the date a book **entered the collection**, not a publication year. The
  INPX carries no publication date at all. Label it "added" in any UI.
- Reindex decisions use a content fingerprint (`InpxSignature`) plus per-archive fingerprints,
  so the same collection from a different magnet does not force a rebuild. **Bump
  `LibraryIndexerService.DocumentSchemaVersion` whenever `BookDocument` changes** — content
  fingerprints cannot detect a schema change, and the new field will silently never index.
- Meilisearch settings are applied on every startup, so filterable/sortable changes do not
  need a reindex. `sortFacetValuesBy` must stay `count`: the default is alphabetical and
  silently truncates capped facets to the alphabetical head.
- Both HTTP clients use `Ipv4FirstHandler`. `localhost` costs ~208ms per connection on a
  machine with VPN adapters; `127.0.0.1` costs ~0.8ms.
- Run it with `docker compose up -d` (adds a `meilisearch` service) plus a `.env` — see
  `.env.example`.

## Architecture

The design constraint that shapes everything: the library is a ~500 GB torrent that must never be fully downloaded.

```
Browser (Vue SPA)                 ASP.NET Core            TorrServe
  MagnetGate → magnet URI ──POST /api/library/inpx──→ add torrent, find *.inpx,
                                                      download (~35 MB)
  Web Worker: parse INPX ←────────── bytes ───────────┘
    → MiniSearch index → IndexedDB
  search/filter/paginate  (100% client-side, no server round-trip)

  download click ────────POST /api/library/download──→ HTTP Range slices of the
                                                       ~2 GB .zip → one entry
```

**The server holds no state.** No database, no disk cache, no book storage. Every request re-adds the torrent to TorrServe and streams. All search intelligence lives in the browser.

### Backend (`MyHomeLib.Web`)

Two endpoints only, both POST, both on [LibraryController.cs](MyHomeLib.Web/Controllers/LibraryController.cs): `/api/library/inpx` and `/api/library/download`. CORS is `AllowAnyOrigin` on `/api/*` so the standalone `file://` build can reach a remote instance.

The download path is the interesting part ([TorrServeClient.cs](MyHomeLib.Web/Services/TorrServe/TorrServeClient.cs)): [HttpRangeStream](MyHomeLib.Web/Services/HttpRangeStream.cs) is a seekable `Stream` where every `Read` issues an HTTP GET with a `Range` header against TorrServe's `/stream` URL. Handing that to `ZipArchive` means .NET's zip reader pulls only the central directory plus the one entry's bytes — TorrServe in turn fetches only the covering torrent pieces. FB2 files are then parsed with `Fb2.Document` purely to recover a nice filename; parse failure falls back to the raw bytes.

YARP is `#if DEBUG` only ([YarpExtensions.cs](MyHomeLib.Web/YarpExtensions.cs)) and static-file serving is non-Development only, so in Development the backend serves no SPA.

`MyHomeLib.Library` (INPX/INP reader) is unused on master — there, INPX parsing happens in TypeScript. On this branch it is revived and is the server-side parser; see the branch section above.

### Frontend (`MyHomeLib.Ui`)

Everything expensive runs in [searchIndex.worker.ts](MyHomeLib.Ui/src/workers/searchIndex.worker.ts) (~900 lines). Message protocol, via [searchIndexWorkerClient.ts](MyHomeLib.Ui/src/services/searchIndexWorkerClient.ts):

| Message | Effect |
|---|---|
| `parse-and-build` | streaming INPX parse → MiniSearch index → persist (buffer is transferred, not copied) |
| `restore` | rebuild index from IndexedDB; validated against `hash` + `signature` |
| `search` | term → year filter → genre facets → genre filter → page slice |
| `clear-persisted` / `clear-all-persisted` | cache eviction |

Three IndexedDB databases, deliberately split so a restore reads one small record instead of walking every book:

- `myhomelib-search-index-meta-cache` — hash, dataset signature, format version, totals
- `myhomelib-search-index-json-cache` — serialized MiniSearch index + compact facet array
- `myhomelib-library-books-<hash12>` — per-library book records

Payloads are gzipped via `CompressionStream` unless the ratio is worse than 0.95, in which case the raw JS object is stored to avoid pointless decompress CPU on restore.

**Bump `PERSISTENCE_FORMAT_VERSION` in the worker whenever the persisted index or facet layout changes** — that is the only thing invalidating stale caches in users' browsers.

Search config: fields `title/authors/series/lang`, `combineWith: "AND"`, `prefix: true`, boosts title 4 / authors 2 / series 1.5. Terms are normalized with `toLocaleLowerCase("ru-RU")` and `ё → е`; the corpus is Russian, so don't swap in a plain `toLowerCase`.

[useLibraryState.ts](MyHomeLib.Ui/src/composables/useLibraryState.ts) is a VueUse `createGlobalState` singleton holding all app state and orchestrating the worker. Components are organized atomic-style (`molecules/` → `organisms/` → `pages/` → `templates/`).

### Build modes

| | normal build | standalone build |
|---|---|---|
| config | `vite.config.ts` | `vite.config.standalone.ts` |
| output | `dist/` → served by the backend | `docs/index.html` → GitHub Pages |
| API base | `""` (same origin) | `https://books-api.alfeg.net:8443`, overridable in the UI, stored in `localStorage` |

The switch is `VITE_STANDALONE`, read by [endpointStore.ts](MyHomeLib.Ui/src/services/endpointStore.ts). Both configs inline every asset (`viteSingleFile`, `assetsInlineLimit: 100 MB`).

CI ([build-standalone-pages.yml](.github/workflows/build-standalone-pages.yml)) rebuilds `docs/index.html` on every push to master and commits it back — hence the recurring `ci: update standalone docs bundle` commits. CI does **not** run tests, lint, typecheck, or `dotnet build`; run those locally before pushing.

## INPX format

An `.inpx` is a ZIP of `.inp` files. Rows are `\n`-separated; **fields are separated by `\x04`, not `|`**. The archive a book lives in is derived from the `.inp` entry name (`fb2-000024-030559.inp` → `fb2-000024-030559.zip`).

Field indexes ([inpxParser.ts](inpx-parser/src/inpxParser.ts) `mapBook`):

```
0 authors  1 genre  2 title  3 series  4 seriesNo  5 file
7 id       9 ext   10 date  11 lang
```

Authors are `:`-separated, each `Surname,First,Middle`, normalized to `First Middle Surname`. Genres are `:`-separated codes resolved to labels via `src/assets/genres.{ru,en}.json`. Encoding is UTF-8 or CP1251 — detected by sampling for mojibake, not declared.

## TorrServe API

`POST /torrents` with `{"action":"add"|"get"|"rem", ...}`. `stat`: 2 = preload, 3 = working, 4 = closed, 5 = in DB. The `data` field is a JSON-encoded **string** containing `{"TorrServer":{"Files":[…]}}`; `TorrServeTorrent` handles both that and the older flat `file_stat` shape. Files only appear once metadata resolves, so `WaitForFilesAsync` polls `get` every 2s. `GET /stream?link=<hash>&index=<id>&play` is the Range-capable endpoint. `GET /echo` returns a plain-text version string.

## Conventions

- Solution is SLNX (`MyHomeLibServer.slnx`) — do not generate a legacy `.sln`.
- .NET 10, nullable enabled, implicit usings, primary constructors for DI.
- TorrServe JSON uses source-generated contexts (`TorrServeJsonContext`) — new DTOs must be registered there.
- User-facing backend strings go through `IStringLocalizer` with `Resources/Controllers/LibraryController.ru.resx`; English is the resource-key fallback.
- TS config sets `strict: true` but disables `strictNullChecks` and `noImplicitAny`; `useLibraryState.ts` carries a file-level `eslint-disable`. Don't treat a clean `npm run ts` as proof of null-safety.
- Prettier: 4-space indent, no semicolons, sorted imports (Vue/TS files); the `inpx-parser` package uses its own 2-space semicolon style.

## Stale documentation — do not trust

[.github/copilot-instructions.md](.github/copilot-instructions.md) describes an architecture that no longer exists: a `MyHomeLib.Torrent` project, DuckDB, SharpZipLib, `LibraryService`/`LibraryBooksCacheService` background services, `GET` endpoints, `/api/library/status`, a `Library:` config section, pipe-delimited INPX rows, disk caching. None of that is real. The backend is MVC controllers, not Minimal API.

[README.md](README.md) is mostly accurate but references `MyHomeLib.Ui/dist-standalone/index.html` (actual: `docs/index.html`), claims the Vite dev server proxies to `localhost:5000` (actual: the remote `books-api.alfeg.net:8443`), and claims the server caches INPX to disk (it does not).

Dev-proxy mismatch: `appsettings.Development.json` points YARP at `localhost:5173` while Vite serves `:5175`.
