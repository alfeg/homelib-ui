# Copilot Instructions for MyHomeLib / FlibustaCli

## Project Overview

**FlibustaCli** is a cross-platform .NET 10 CLI tool for searching and downloading books from the [Flibusta](https://flibusta.is/) e-book library via BitTorrent. Libraries are distributed as magnet-linked torrents containing an INPX index file and ZIP book archives.

## Solution Structure

```
MyHomeLibServer.slnx
├── FlibustaCli/            # CLI entry point (Exe, net10.0, assembly: flibustaCli)
├── MyHomeLib.Torrent/      # Torrent + business logic (Library, net10.0)
└── MyHomeLib.Library/      # Book data models + INPX parser (Library, net10.0)
```

### Project Dependencies
- `FlibustaCli` → `MyHomeLib.Torrent` → `MyHomeLib.Library`

## Key Technologies

| Concern | Library |
|---|---|
| CLI framework | `Spectre.Console.Cli` (v0.49.1) |
| Torrent client | `MonoTorrent` (v3.0.2) |
| Book index storage | `Parquet.Net` (v5.1.0) via `ParquetSerializer` |
| Book index parsing | `CsvHelper` (v33.0.1) + `SharpZipLib` (v1.4.2) |
| FB2 book parsing | `Fb2.Document` (v2.4.0) |
| Human-readable output | `Humanizer.Core` (v2.14.1) |
| DI / configuration | `Microsoft.Extensions.*` via `Microsoft.NET.Sdk.Web` |

## Architecture & Data Flow

1. **Add Library**: User provides a magnet URI → `LibraryIndexer.IndexLibrary()` downloads the `.inpx` file via torrent → parses it with `InpxReader` → serializes book metadata as a `.parquet` file in the cache directory.
2. **Search**: `LibraryIndexer.SearchLibrary()` reads the `.parquet` file and filters `BookItem` records by title/author/language.
3. **Download**: `DownloadManager.DownloadFile()` streams the target ZIP archive from the torrent → extracts the specific book file (`.fb2` or other) → saves to disk.

### INPX Format
An INPX file is a ZIP archive containing `.inp` files (pipe-delimited CSV rows) with book metadata, plus `collection.info` and `version.info` metadata files.

### Cache Layout (configured via `AppConfig.CacheDirectory`)
```
cache/
  torrents/
    <infohash>/          # MonoTorrent download directory
    <infohash>.torrent   # Saved torrent metadata
    <infohash>.parquet   # Serialized book index
```

## Configuration (`appsettings.json` / environment variables)

```json
{
  "CacheDirectory": "./cache",
  "AnnounceUrls": ["http://<tracker-host>/announce"],
  "SpecialPeers": ["ipv4://<peer-host>:<port>/"]
}
```

All settings map to `AppConfig`. Environment variables override `appsettings.json` (standard `IConfiguration` binding).

## CLI Commands

| Command | Alias | Description |
|---|---|---|
| `lib list` | `lib-list` | List indexed libraries |
| `lib add <magnet>` | `lib-add <magnet>` | Add a library by magnet URI |
| `search <name>` | — | Search books by title or author |
| `download <id>` | `get <id>` | Download a book by its numeric ID |

### Search Options
- `-l / --library <hash>` — target a specific library hash
- `-m / --max <n>` — max results (default 20, cap 100)
- `-g / --language <lang>` — filter by language code (e.g. `ru`, `en`)
- `-a / --author <name>` — restrict match to author field

## Core Classes

- **`LibraryIndexer`** (`MyHomeLib.Torrent`) — orchestrates indexing, listing, and searching libraries.
- **`DownloadManager`** (`MyHomeLib.Torrent`) — wraps MonoTorrent `ClientEngine`; handles streaming download, file search, and cache eviction (archives older than 1 hour are deleted).
- **`InpxReader`** (`MyHomeLib.Library`) — async streaming parser for INPX archives.
- **`BookItem`** (`MyHomeLib.Library`) — Parquet-serializable record: `Id`, `Authors`, `Title`, `Genre`, `Series`, `Lang`, `Ext`, `ArchiveFile`, `File`, `Size`, `Date`, `Deleted`, etc.
- **`AppConfig`** (`MyHomeLib.Torrent`) — strongly-typed configuration POCO.
- **`Gate`** (`MyHomeLib.Torrent`) — simple async semaphore used to serialize concurrent library reads.
- **`PartialStreamingRequester`** (`MyHomeLib.Torrent`) — custom MonoTorrent piece requester for partial/streaming downloads.

## Build & CI

Build from the `FlibustaCli` working directory:

```bash
dotnet restore
dotnet build -c release
```

CI (GitHub Actions `.github/workflows/dotnet.yaml`) builds on **ubuntu-latest**, **macos-latest**, and **windows-latest** and publishes two artifacts per OS:
- **small** — framework-dependent single-file (`--no-self-contained`)
- **portable** — self-contained, trimmed, compressed single-file

Releases are created automatically when the `<Version>` in `FlibustaCli/FlibustaCli.csproj` differs from the latest GitHub release tag.

## Docker

A `docker-compose.yml` is provided for deploying a companion server component (`myhomelib` image):
- Port `16000:80`
- Volumes: `homelib` for SQLite DB, a bind-mount for the library index file
- Integrates with a Traefik reverse proxy via the `traefik_default` network

## Coding Conventions

- **Nullable reference types** enabled (`<Nullable>enable</Nullable>`) — always annotate nullability.
- **Implicit usings** enabled — avoid redundant `using` directives.
- Solution file uses the modern **SLNX** format (`MyHomeLibServer.slnx`). Do not regenerate a legacy `.sln` file.
- DI is constructor-injected; services are registered as `Singleton` in `TorrentsServiceExtension.AddTorrents()`.
- Commands inherit `AsyncCommand<TSettings>` from `Spectre.Console.Cli`.
- Use `AnsiConsole` for all console output (not `Console.Write*`).
- New commands must be registered in `Program.cs` via `app.Configure(...)`.
- Prefer `IAsyncEnumerable` for streaming data (e.g. search results, book parsing).
