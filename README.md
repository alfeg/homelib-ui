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

# Edit appsettings.json: set Library:DownloadsDirectory and Torrent:TorrServeUrl
dotnet run --project MyHomeLib.Web
```

Open `http://localhost:5000` (or whichever port ASP.NET assigns — check the console output).

On first run the app will download and parse the INPX index. This takes a few minutes depending on your connection. A status bar in the UI shows progress including download speed, peer count, and cache progress. Subsequent starts are instant.

## Configuration

All settings go under the `Library` and `Torrent` sections in `appsettings.json`, or as environment variables using double-underscore as separator (e.g. `Library__DownloadsDirectory=/books`).

### `Library` section

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `MagnetUri` | Yes* | — | Magnet link for the library torrent. Required unless `InpxPath` is set. |
| `DownloadsDirectory` | Yes | — | Directory where downloaded books and database files are saved. |
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

1. The UI shows a status bar with live progress.
2. MyHomeLib asks TorrServe to register the library magnet link, then waits for metadata.
3. Once TorrServe has the torrent metadata, it downloads only the `.inpx` file (~100 MB).  
   The status bar shows download speed (↓/↑), active peer / seeder counts, and a cache progress bar.
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
| `MyHomeLib.Torrent/TorrServeClient.cs` | HTTP wrapper for TorrServe API (add torrent, wait for files, stream URL, rich stats) |
| `MyHomeLib.Torrent/HttpRangeStream.cs` | Seekable `Stream` backed by HTTP Range requests — lets `ZipArchive` read a remote ZIP without downloading the whole file |
| `MyHomeLib.Torrent/DownloadManager.cs` | Orchestrates search and download via TorrServe; exposes `TorrentStats` (speed, peers, progress) |
| `MyHomeLib.Web/LibraryService.cs` | BackgroundService — downloads/locates the INPX, streams it into the DuckDB index |
| `MyHomeLib.Web/BookSearchIndex.cs` | File-backed DuckDB index — builds once, reused across restarts |
| `MyHomeLib.Web/DownloadQueueService.cs` | BackgroundService — persistent download queue backed by DuckDB; samples TorrServe stats every 3 s |
| `Dockerfile` | Multi-stage build: .NET 10 SDK → ASP.NET runtime, port 8080 |
| `docker-compose.yml` | Starts TorrServe + MyHomeLib with shared named volumes |

## Memory usage

The book index (545 000 records) is stored in a file-backed DuckDB database. Only queried rows are loaded into RAM. There is no in-memory list of books. A typical idle footprint is ~100–150 MB, making it suitable for low-resource VPS deployments.

## Downloading books

1. Search for a title, author, or series.
2. Click ⬇ next to a result to queue it.
3. Go to **Downloads** to watch progress and fetch the file when ready.

TorrServe downloads only the torrent pieces containing the requested book's entry inside its ZIP archive. A typical download takes a few seconds.

---

# MyHomeLib — Документация на русском

Самохостируемое веб-приложение (Blazor Server) для поиска и скачивания книг с библиотеки [Флибуста](https://flibusta.is/), распространяемой через BitTorrent.

## Как это работает

Библиотека Флибусты — один большой торрент (~400 ГБ), содержащий тысячи ZIP-архивов с книгами в формате FB2, а также INPX-файл с метаданными всех книг.

MyHomeLib **не скачивает весь торрент**. Вместо этого:

1. **При первом запуске** — скачивает только INPX-файл (~100 МБ) через [TorrServe](https://github.com/YouROK/TorrServer), парсит его и строит полнотекстовый поисковый индекс в файловой базе данных [DuckDB](https://duckdb.org/). При следующих запусках база переиспользуется — парсинг пропускается.
2. **При поиске** — запрос к FTS-индексу DuckDB (стеммер Snowball для русского, ранжирование BM25). Результаты выдаются мгновенно.
3. **При скачивании** — через TorrServe HTTP Range запросы скачивается только нужный ZIP-архив с запрошенной книгой (~5–30 МБ вместо всего архива).

## Требования

- [TorrServe](https://github.com/YouROK/TorrServer) — берёт на себя всю работу с BitTorrent. MyHomeLib обращается к нему по HTTP.
- ~500 МБ на диске для INPX-файла и базы данных DuckDB
- Папка для сохранения скачанных книг

## Быстрый старт через Docker Compose

Самый простой способ запустить всё вместе:

```bash
git clone <репозиторий>
cd homelib-ui
docker compose up -d
```

`docker-compose.yml` поднимает два сервиса:
- **torrserve** — TorrServe на порту `8090`
- **myhomelib** — веб-приложение на порту `8080`

Откройте в браузере: [http://localhost:8080](http://localhost:8080)

Книги и поисковая база хранятся в именованном Docker-томе, смонтированном как `/data/books` внутри контейнера.

### Изменение настроек

Отредактируйте `docker-compose.yml` (или задайте переменные окружения):

```yaml
environment:
  Library__MagnetUri: "magnet:?xt=urn:btih:..."   # магнет-ссылка на торрент библиотеки
  Library__DownloadsDirectory: /data/books          # куда сохранять книги
  Torrent__TorrServeUrl: http://torrserve:8090      # адрес TorrServe
```

> Разделитель для вложенных ключей в переменных окружения — двойное подчёркивание (`__`).

## Локальный запуск (без Docker)

### Предварительные требования

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Запущенный TorrServe. Скачайте бинарник со страницы [релизов TorrServe](https://github.com/YouROK/TorrServer/releases) и запустите:
  ```bash
  # Linux/macOS
  ./TorrServer-linux-amd64
  # Windows
  TorrServer-windows-amd64.exe
  ```
  По умолчанию TorrServe слушает на `http://localhost:8090`.

### Шаги

```bash
git clone <репозиторий>
cd homelib-ui

dotnet run --project MyHomeLib.Web
```

Откройте [http://localhost:5000](http://localhost:5000) (точный порт — в выводе консоли).

При первом запуске приложение скачает и распарсит INPX-индекс (несколько минут в зависимости от скорости соединения). Статусная строка показывает скорость загрузки (↓/↑), количество пиров и прогресс кеширования. При последующих запусках индекс готов за секунды.

## Конфигурация

Настройки задаются в `appsettings.json` или через переменные окружения (разделитель `__`).

### Секция `Library`

| Ключ | Обязательно | По умолчанию | Описание |
|------|-------------|--------------|----------|
| `MagnetUri` | Да* | — | Магнет-ссылка на торрент библиотеки |
| `DownloadsDirectory` | Да | — | Папка для скачанных книг и файлов базы данных |
| `InpxPath` | Нет | — | Путь к уже скачанному `.inpx`-файлу. Если указан — торрент для индексации не используется |
| `QueueDbPath` | Нет | `<DownloadsDirectory>/queue.db` | Путь к базе очереди скачивания (DuckDB) |
| `LibraryDbPath` | Нет | `<путь_к_inpx>.db` | Путь к базе поискового индекса (DuckDB) |

\* `MagnetUri` не нужен, если указан `InpxPath` с уже существующим файлом.

### Секция `Torrent`

| Ключ | Обязательно | По умолчанию | Описание |
|------|-------------|--------------|----------|
| `TorrServeUrl` | Да | — | URL экземпляра TorrServe, например `http://127.0.0.1:8090` |

### Пример `appsettings.json`

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

## Первый запуск

1. Приложение открывается с статусной строкой в нижней части страницы.
2. MyHomeLib регистрирует магнет-ссылку в TorrServe и ждёт метаданных торрента.
3. TorrServe скачивает только `.inpx`-файл (~100 МБ). В статусной строке отображаются скорость загрузки (↓/↑), количество активных пиров и сидеров, прогресс кеширования.
4. INPX-файл парсится — ~545 000 записей о книгах записываются в DuckDB.
5. Строится полнотекстовый поисковый индекс (русский стеммер Snowball) по названию, автору, серии и ключевым словам.
6. Строка поиска становится активной.

**При повторном запуске**: если база данных уже содержит книги, шаги 2–5 полностью пропускаются. Индекс готов за несколько секунд.

## Поиск и скачивание книг

1. Введите название, автора или серию в строке поиска.
2. Нажмите ⬇ рядом с нужной книгой — она добавится в очередь скачивания.
3. Перейдите в раздел **Downloads**, чтобы отследить прогресс и скачать файл.

TorrServe загружает только те фрагменты торрента, которые содержат нужную книгу внутри ZIP-архива. Типичное время скачивания — несколько секунд.

## Использование памяти

Поисковый индекс (545 000 книг) хранится в файловой базе DuckDB. В оперативную память загружаются только результаты запросов — никакого списка всех книг в памяти нет. Типичное потребление памяти в режиме ожидания — ~100–150 МБ, что делает приложение пригодным для запуска на бюджетных VPS.

## Структура проекта

```
MyHomeLibServer.slnx
├── MyHomeLib.Library/      # Модель данных книги + парсер INPX
├── MyHomeLib.Torrent/      # Клиент TorrServe, менеджер загрузок, HTTP Range Stream
└── MyHomeLib.Web/          # Blazor Server UI, очередь загрузок, поисковый индекс DuckDB
```

