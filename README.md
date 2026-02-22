# MyHomeLib

A self-hosted Blazor web app for searching and downloading books from the [Flibusta](https://flibusta.is/) e-book library distributed as a BitTorrent.

## How it works

The Flibusta library is distributed as a single large magnet torrent (~400 GB) containing thousands of ZIP archives, each holding hundreds of FB2 books, plus an INPX index file with metadata for all books.

MyHomeLib never downloads the full torrent. Instead:

1. **On first start** — it downloads only the INPX index file (~100 MB) via [TorrServe](https://github.com/YouROK/TorrServer), parses it, and builds a full-text search index stored in a local [DuckDB](https://duckdb.org/) database file. Subsequent starts reuse the database; parsing is skipped.
2. **On search** — queries the DuckDB FTS index (Russian Snowball stemmer, BM25 ranking) and returns results instantly.
3. **On download** — streams only the specific ZIP archive containing the requested book via TorrServe HTTP Range requests. Only the torrent pieces covering that book's byte range are downloaded (~5–30 MB instead of the full archive).

## Requirements

- [TorrServe](https://github.com/YouROK/TorrServer) — handles all BitTorrent work. MyHomeLib connects to it over HTTP.
- ~500 MB disk for the INPX + DuckDB search index
- A directory to save downloaded books

## Quick start with Docker Compose

The simplest way to run everything together:

```bash
docker compose up -d
```

The default `docker-compose.yml` starts both TorrServe and MyHomeLib. Books and the search index are stored in a named volume mounted at `/data/books` inside the container.

To customise the magnet URI or download directory, set environment variables or edit `docker-compose.yml` directly (see [Configuration](#configuration)).

## Running locally (development)

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- TorrServe running on `http://localhost:8090` (download a binary from the [TorrServe releases](https://github.com/YouROK/TorrServer/releases))

### Steps

```bash
git clone <repo>
cd homelib-ui

# Edit appsettings.json: set Library:DownloadsDirectory to a real path
dotnet run --project MyHomeLib.Web
```

Open `http://localhost:5000` (or whichever port ASP.NET assigns — check the console output).

On first run the app will download and parse the INPX index. This takes a few minutes depending on your connection. A spinner in the UI shows progress. Subsequent starts are instant.

## Configuration

All settings go under the `Library` and `Torrent` sections in `appsettings.json`, or as environment variables using double-underscore as separator (e.g. `Library__DownloadsDirectory=/books`).

### `Library` section

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `MagnetUri` | Yes* | — | Magnet link for the library torrent. Required unless `InpxPath` is set. |
| `DownloadsDirectory` | Yes | — | Directory where downloaded books and the INPX file are saved. |
| `InpxPath` | No | — | Absolute path to an already-downloaded `.inpx` file. When set, the torrent is not used for indexing. |
| `QueueDbPath` | No | `<DownloadsDirectory>/queue.db` | Path for the download queue DuckDB file. |
| `LibraryDbPath` | No | `<inpx_path>.db` | Path for the book search index DuckDB file. |

\* `MagnetUri` is not required if `InpxPath` points to an existing file.

### `Torrent` section

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `TorrServeUrl` | Yes | — | URL of the TorrServe instance, e.g. `http://torrserve:8090`. |

### Example `appsettings.json`

```json
{
  "Library": {
    "MagnetUri": "magnet:?xt=urn:btih:8beb62f8e5db5ebe96d6864d138320425fea81c7&...",
    "DownloadsDirectory": "/data/books"
  },
  "Torrent": {
    "TorrServeUrl": "http://localhost:8090"
  }
}
```

## Docker Compose reference

```yaml
services:
  torrserve:
    image: yourok/torrserver:latest
    restart: unless-stopped
    ports:
      - "8090:8090"
    volumes:
      - torrserve_data:/opt/torrserver

  myhomelib:
    build: .
    restart: unless-stopped
    ports:
      - "8080:8080"
    environment:
      Library__MagnetUri: "magnet:?xt=urn:btih:8beb62f8e5db5ebe96d6864d138320425fea81c7&..."
      Library__DownloadsDirectory: /data/books
      Torrent__TorrServeUrl: http://torrserve:8090
    volumes:
      - books_data:/data/books
    depends_on:
      - torrserve

volumes:
  torrserve_data:
  books_data:
```

## First run

1. The UI shows a spinner with a live status line.
2. MyHomeLib asks TorrServe to register the library magnet link, then waits for metadata.
3. Once TorrServe has the torrent metadata, it downloads only the `.inpx` file (~100 MB).
4. The INPX is parsed and all ~545 000 book records are written to a DuckDB file.
5. A full-text search index (Russian Snowball stemmer) is built over title, author, series, and keywords.
6. The search box becomes active.

**On restart**: if the DuckDB file exists and already contains books, steps 2–5 are skipped entirely. The index is ready within seconds.

## Project structure

```
MyHomeLibServer.slnx
├── MyHomeLib.Library/      # Book data model + INPX parser
├── MyHomeLib.Torrent/      # TorrServe client, download manager, HTTP range stream
└── MyHomeLib.Web/          # Blazor Server UI, download queue, DuckDB search index
```

### Key files

| File | Purpose |
|------|---------|
| `MyHomeLib.Torrent/TorrServeClient.cs` | HTTP wrapper for TorrServe API (add torrent, wait for files, stream URL) |
| `MyHomeLib.Torrent/HttpRangeStream.cs` | Seekable `Stream` backed by HTTP Range requests — lets `ZipArchive` read a remote ZIP without downloading the whole file |
| `MyHomeLib.Torrent/DownloadManager.cs` | Orchestrates search and download via TorrServe |
| `MyHomeLib.Web/LibraryService.cs` | Downloads/locates the INPX, streams it into the DuckDB index |
| `MyHomeLib.Web/BookSearchIndex.cs` | File-backed DuckDB index — builds once, reused across restarts |
| `MyHomeLib.Web/DownloadQueueService.cs` | Background service: persistent download queue backed by DuckDB |

## Memory usage

The book index (545 000 records) is stored in a file-backed DuckDB database. Only queried rows are loaded into RAM. There is no in-memory list of books. A typical idle footprint is ~100–150 MB.

## Downloading books

1. Search for a title, author, or series.
2. Click ⬇ next to a result to queue it.
3. Go to **Downloads** to watch progress and fetch the file when ready.

TorrServe downloads only the torrent pieces containing the requested book's entry inside its ZIP archive. A typical download takes a few seconds.
