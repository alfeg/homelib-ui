import { createGlobalState, useLocalStorage } from "@vueuse/core"

import { computed } from "vue"

export type AppLocale = "ru" | "en"

function detectDefaultLocale(): AppLocale {
    if (typeof navigator === "undefined") return "ru"
    return navigator.language?.toLowerCase().startsWith("ru") ? "ru" : "en"
}

const localeStorage = useLocalStorage<AppLocale>("mhl-ui-locale", detectDefaultLocale())

const messages = {
    ru: {
        app: { title: "Поиск MyHomeLib" },
        common: {
            hash: "Хеш",
            versionBooks: "Версия {version} · {count}",
            lastUpdate: "Последнее обновление: {value}",
            cache: "кэш",
            backend: "сервер",
            language: "Язык",
            phaseInit: "init",
            phaseLoading: "loading",
            phaseIndexing: "indexing",
            phaseReady: "ready",
        },
        buttons: {
            reindex: "Переиндексировать",
            reindexing: "Переиндексация...",
            fullReset: "Полный сброс",
            previous: "Назад",
            next: "Вперёд",
            download: "Скачать",
            downloading: "Скачивание...",
        },
        search: {
            placeholder: "Поиск по названию, автору, серии, языку...",
            showingOf: "Показано {filtered} из {total} книг",
        },
        genres: {
            title: "Жанры",
            reset: "Сбросить",
            total: "Всего жанров: {count}",
            notFound: "Жанры не найдены.",
            noGenre: "Без жанра",
        },
        table: {
            title: "Название",
            authors: "Авторы",
            series: "Серия",
            genres: "Жанры",
            lang: "Язык",
            date: "Дата",
            action: "Действие",
            noBooks: "Книги не найдены.",
            range: "Показано {start}-{end} из {total} книг",
            page: "Страница {page} / {totalPages}",
        },
        gate: {
            title: "Подключите библиотеку",
            titleLoading: "Загружаем библиотеку",
            subtitle: "Вставьте magnet URI или загрузите .torrent файл для открытия библиотеки.",
            openLibrary: "Открыть библиотеку",
            loading: "Загрузка...",
            chooseTorrent: "Выбрать .torrent файл",
            lucky: "Мне повезёт",
            dragHint: "Вы также можете перетащить .torrent файл на эту карточку.",
            dismiss: "Закрыть",
        },
        status: {
            indexing: "Индексация книг: {processed}/{total} ({percent}%)",
            parsingWithTotal: "Парсинг INPX: {processed}/{total} ({percent}%)",
            parsingSimple: "Парсинг INPX: {percent}%",
            loadingCache: "Загрузка локального кэша...",
            clearingLocal: "Очистка локального кэша и поискового индекса...",
            downloadingInpxTotal: "Загрузка INPX: {downloaded} / {total} ({percent}%)",
            downloadingInpxSimple: "Загрузка INPX: загружено {downloaded}",
            loadedFromCache: "Загружено из локального кэша.",
            libraryIndexed: "Библиотека локально проиндексирована из INPX.",
            cachedLibraryRestoring: "Кэшированная библиотека загружена. Восстановление поискового индекса...",
            cachedIndexStale: "Кэшированный поисковый индекс устарел. Пересборка...",
            cachedIndexMissing: "Кэшированный поисковый индекс недоступен. Пересборка...",
            buildingIndexInit: "Сборка поискового индекса... 0/{total} (0%)",
            reindexDownloading: "Локальная переиндексация: загрузка INPX с сервера...",
            loadingDownloading: "Загрузка библиотеки: загрузка INPX с сервера...",
            inpxDownloadedParsing: "INPX загружен. Парсинг на клиенте...",
            libraryDataLoadedBuilding: "Данные библиотеки загружены. Сборка поискового индекса...",
            reindexFailed: "Локальная переиндексация не удалась: невозможно загрузить или разобрать INPX.",
            loadFailed: "Ошибка загрузки библиотеки: невозможно загрузить или разобрать INPX.",
            reindexClearing: "Локальная переиндексация: очистка кэша библиотеки и поискового индекса...",
            localCacheCleared: "Локальный кэш очищен. Загрузка INPX для пересборки...",
            reindexTryAgain: "Локальная переиндексация не удалась. Попробуйте снова.",
            savedLibraryLoadFailedTryReindex: "Не удалось загрузить сохранённую библиотеку. Попробуйте переиндексацию.",
        },
        error: {
            magnetRequired: "Требуется magnet URI.",
            selectTorrent: "Пожалуйста, выберите .torrent файл.",
            invalidMagnet: "Некорректный magnet URI.",
            parseTorrent: "Не удалось разобрать .torrent файл.",
            searchWorkerFailed: "Ошибка воркера поискового индекса.",
            inpxParseFailed: "Не удалось разобрать INPX payload.",
            reindexFailed: "Не удалось выполнить переиндексацию.",
            downloadFailed: "Не удалось скачать файл.",
            savedLibraryFailed: "Не удалось загрузить сохранённую библиотеку.",
            inpxLoadFailed: "Не удалось загрузить библиотеку из INPX.",
        },
    },
    en: {
        app: { title: "MyHomeLib Search" },
        common: {
            hash: "Hash",
            versionBooks: "Version {version} · {count}",
            lastUpdate: "Last update: {value}",
            cache: "cache",
            backend: "backend",
            language: "Language",
            phaseInit: "init",
            phaseLoading: "loading",
            phaseIndexing: "indexing",
            phaseReady: "ready",
        },
        buttons: {
            reindex: "Reindex",
            reindexing: "Reindexing...",
            fullReset: "Full Reset",
            previous: "Previous",
            next: "Next",
            download: "Download",
            downloading: "Downloading...",
        },
        search: {
            placeholder: "Search title, author, series, language...",
            showingOf: "Showing {filtered} of {total} books",
        },
        genres: {
            title: "Genres",
            reset: "Reset",
            total: "Total genres: {count}",
            notFound: "No genres found.",
            noGenre: "No genre",
        },
        table: {
            title: "Title",
            authors: "Authors",
            series: "Series",
            genres: "Genres",
            lang: "Lang",
            date: "Date",
            action: "Action",
            noBooks: "No books found.",
            range: "Showing {start}-{end} of {total} books",
            page: "Page {page} / {totalPages}",
        },
        gate: {
            title: "Connect your library",
            titleLoading: "Loading library...",
            subtitle: "Paste a magnet URI or upload a .torrent file to open this library.",
            openLibrary: "Open Library",
            loading: "Loading...",
            chooseTorrent: "Choose .torrent file",
            lucky: "I'm feeling lucky",
            dragHint: "You can also drag and drop a .torrent file anywhere on this card.",
            dismiss: "Dismiss",
        },
        status: {
            indexing: "Indexing: {processed}/{total} ({percent}%)",
            parsingWithTotal: "Parsing INPX: {processed}/{total} ({percent}%)",
            parsingSimple: "Parsing INPX: {percent}%",
            loadingCache: "Loading local cache...",
            clearingLocal: "Clearing local cache and search index...",
            downloadingInpxTotal: "Downloading INPX payload: {downloaded} / {total} ({percent}%)",
            downloadingInpxSimple: "Downloading INPX payload: {downloaded} downloaded",
            loadedFromCache: "Loaded from local cache.",
            libraryIndexed: "Library indexed locally from INPX.",
            cachedLibraryRestoring: "Cached library loaded. Restoring search index...",
            cachedIndexStale: "Cached search index is stale. Rebuilding...",
            cachedIndexMissing: "Cached search index unavailable. Rebuilding...",
            buildingIndexInit: "Building search index... 0/{total} (0%)",
            reindexDownloading: "Reindexing locally: downloading INPX from backend...",
            loadingDownloading: "Loading library: downloading INPX from backend...",
            inpxDownloadedParsing: "INPX downloaded. Parsing on client...",
            libraryDataLoadedBuilding: "Library data loaded. Building search index...",
            reindexFailed: "Local reindex failed: unable to download or parse INPX.",
            loadFailed: "Library load failed: unable to download or parse INPX.",
            reindexClearing: "Reindexing locally: clearing cached library and search index...",
            localCacheCleared: "Local cache cleared. Downloading INPX for rebuild...",
            reindexTryAgain: "Local reindex failed. Please try again.",
            savedLibraryLoadFailedTryReindex: "Failed to load saved library. Please try reindexing.",
        },
        error: {
            magnetRequired: "Magnet URI is required.",
            selectTorrent: "Please choose a .torrent file.",
            invalidMagnet: "Invalid magnet URI.",
            parseTorrent: "Failed to parse .torrent file.",
            searchWorkerFailed: "Search index worker failed.",
            inpxParseFailed: "Failed to parse INPX payload.",
            reindexFailed: "Failed to reindex.",
            downloadFailed: "Download failed.",
            savedLibraryFailed: "Failed to load saved library.",
            inpxLoadFailed: "Failed to load library from INPX.",
        },
    },
}

function normalizeLocale(value: string): AppLocale {
    return value === "en" ? "en" : "ru"
}

function getByPath(source: Record<string, unknown>, path: string): string | undefined {
    const parts = path.split(".")
    let cursor: unknown = source
    for (let i = 0; i < parts.length; i += 1) {
        if (cursor == null || typeof cursor !== "object") return undefined
        cursor = (cursor as Record<string, unknown>)[parts[i]]
    }
    return typeof cursor === "string" ? cursor : undefined
}

function interpolate(template: string, params?: Record<string, unknown>) {
    if (!params) return template
    return template.replace(/\{(\w+)\}/g, (_, key) => String(params[key] ?? ""))
}

export function translate(key: string, params?: Record<string, unknown>): string {
    const locale = normalizeLocale(localeStorage.value)
    const localized = getByPath(messages[locale], key)
    if (localized) return interpolate(localized, params)
    const fallback = getByPath(messages.en, key) ?? key
    return interpolate(fallback, params)
}

export const localeRef = computed<AppLocale>(() => normalizeLocale(localeStorage.value))

export function setLocale(next: string) {
    localeStorage.value = normalizeLocale(next)
}

export function getCurrentLocale(): AppLocale {
    return normalizeLocale(localeStorage.value)
}

export const useI18nState = createGlobalState(() => ({
    locale: localeRef,
    setLocale,
    t: translate,
}))
