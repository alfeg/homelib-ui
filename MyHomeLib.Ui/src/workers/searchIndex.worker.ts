import MiniSearch from "minisearch"

import { parseInpxBuffer } from "./inpxParser"

const DEFAULT_INDEX_BATCH_SIZE = 1024
const MIN_INDEX_BATCH_SIZE = 500
const MAX_INDEX_BATCH_SIZE = 2000
const PERSISTENCE_DB_NAME = "myhomelib-search-index-cache"
const PERSISTENCE_STORE_NAME = "indexes"
// Version 6: switched from FlexSearch chunk export to MiniSearch toJSON snapshot.
// Bumping the version drops the old object store so stale FlexSearch data is cleared.
const PERSISTENCE_DB_VERSION = 6
const LIBRARY_CACHE_DB_NAME = "myhomelib-library-cache"
const LIBRARY_CACHE_STORE_NAME = "libraries"
const LIBRARY_CACHE_DB_VERSION = 1

let index = null // MiniSearch instance
let booksById = new Map()
let activeHash = ""
let persistenceDbPromise = null
let libraryCacheDbPromise = null

// Normalise a term: Cyrillic-aware lowercase + collapse e/yo.
// Returning null causes MiniSearch to skip the token.
const processTerm = (term) => {
    const normalized = term.toLocaleLowerCase("ru-RU").replace(/ё/g, "е")
    return normalized.length > 0 ? normalized : null
}

const INDEX_OPTIONS = {
    fields: ["title", "authors", "series", "lang", "file"],
    idField: "id",
    storeFields: [],
    processTerm,
}

// AND: every query token must appear in the document.
// BM25 then ranks documents that match more / higher-boost fields first.
const SEARCH_OPTIONS = {
    prefix: true,
    combineWith: "AND",
    boost: { title: 4, authors: 2, series: 1.5, lang: 1, file: 1 },
}

const NO_GENRE_CODE = "__no_genre__"

function toIndexDoc(book) {
    return {
        id: String(book.id),
        title: String(book.title ?? ""),
        authors: String(book.authors ?? ""),
        series: String(book.series ?? ""),
        lang: String(book.lang ?? ""),
        file: String(book.file ?? ""),
    }
}

function createIndex() {
    return new MiniSearch(INDEX_OPTIONS)
}

function openPersistenceDb() {
    if (persistenceDbPromise) {
        return persistenceDbPromise
    }

    persistenceDbPromise = new Promise((resolve, reject) => {
        const request = indexedDB.open(PERSISTENCE_DB_NAME, PERSISTENCE_DB_VERSION)

        request.onupgradeneeded = () => {
            const db = request.result
            // Drop old store (clears any stale FlexSearch data from prior versions)
            if (db.objectStoreNames.contains(PERSISTENCE_STORE_NAME)) {
                db.deleteObjectStore(PERSISTENCE_STORE_NAME)
            }
            db.createObjectStore(PERSISTENCE_STORE_NAME, { keyPath: "hash" })
        }

        request.onsuccess = () => resolve(request.result)
        request.onerror = () => reject(request.error ?? new Error("Failed to open search index persistence DB."))
    })

    return persistenceDbPromise
}

async function readPersistedIndex(hash) {
    const db = await openPersistenceDb()

    return new Promise((resolve, reject) => {
        const tx = db.transaction(PERSISTENCE_STORE_NAME, "readonly")
        const request = tx.objectStore(PERSISTENCE_STORE_NAME).get(hash)

        request.onsuccess = () => resolve(request.result ?? null)
        request.onerror = () => reject(request.error ?? new Error("Failed to read persisted search index."))
    })
}

async function writePersistedIndex(hash, signature, indexJson, total, metadata) {
    const db = await openPersistenceDb()

    return new Promise((resolve, reject) => {
        const tx = db.transaction(PERSISTENCE_STORE_NAME, "readwrite")
        tx.objectStore(PERSISTENCE_STORE_NAME).put({
            hash,
            signature,
            indexJson, // MiniSearch JSON snapshot string
            total,
            metadata: metadata ?? null,
            updatedAt: new Date().toISOString(),
        })

        tx.oncomplete = () => resolve(undefined)
        tx.onerror = () => reject(tx.error ?? new Error("Failed to persist search index."))
    })
}

function openLibraryCacheDb() {
    if (libraryCacheDbPromise) {
        return libraryCacheDbPromise
    }

    libraryCacheDbPromise = new Promise((resolve, reject) => {
        const request = indexedDB.open(LIBRARY_CACHE_DB_NAME, LIBRARY_CACHE_DB_VERSION)

        request.onupgradeneeded = () => {
            const db = request.result
            if (!db.objectStoreNames.contains(LIBRARY_CACHE_STORE_NAME)) {
                db.createObjectStore(LIBRARY_CACHE_STORE_NAME, { keyPath: "hash" })
            }
        }

        request.onsuccess = () => resolve(request.result)
        request.onerror = () => reject(request.error ?? new Error("Failed to open library cache DB."))
    })

    return libraryCacheDbPromise
}

async function readPersistedLibraryBooks(hash) {
    const db = await openLibraryCacheDb()

    return new Promise((resolve, reject) => {
        const tx = db.transaction(LIBRARY_CACHE_STORE_NAME, "readonly")
        const request = tx.objectStore(LIBRARY_CACHE_STORE_NAME).get(hash)

        request.onsuccess = () => {
            const record = request.result ?? null
            const books = Array.isArray(record?.books) ? record.books : []
            resolve(books)
        }
        request.onerror = () => reject(request.error ?? new Error("Failed to read library books from cache DB."))
    })
}

async function writePersistedLibraryBooks(hash, books) {
    const db = await openLibraryCacheDb()

    return new Promise((resolve, reject) => {
        const tx = db.transaction(LIBRARY_CACHE_STORE_NAME, "readwrite")
        tx.objectStore(LIBRARY_CACHE_STORE_NAME).put({ hash, books })

        tx.oncomplete = () => resolve(undefined)
        tx.onerror = () => reject(tx.error ?? new Error("Failed to write library books to cache."))
    })
}

async function deletePersistedLibraryBooks(hash) {
    const db = await openLibraryCacheDb()

    return new Promise((resolve, reject) => {
        const tx = db.transaction(LIBRARY_CACHE_STORE_NAME, "readwrite")
        tx.objectStore(LIBRARY_CACHE_STORE_NAME).delete(hash)

        tx.oncomplete = () => resolve(undefined)
        tx.onerror = () => reject(tx.error ?? new Error("Failed to delete library books from cache."))
    })
}

async function deletePersistedIndex(hash) {
    const db = await openPersistenceDb()

    return new Promise((resolve, reject) => {
        const tx = db.transaction(PERSISTENCE_STORE_NAME, "readwrite")
        tx.objectStore(PERSISTENCE_STORE_NAME).delete(hash)

        tx.oncomplete = () => resolve(undefined)
        tx.onerror = () => reject(tx.error ?? new Error("Failed to clear persisted search index."))
    })
}

function resetInMemoryIndex() {
    index = null
    booksById = new Map()
    activeHash = ""
}

function resolveBatchSize(value) {
    const parsed = Number.parseInt(value, 10)

    if (!Number.isFinite(parsed)) {
        return DEFAULT_INDEX_BATCH_SIZE
    }

    return Math.min(MAX_INDEX_BATCH_SIZE, Math.max(MIN_INDEX_BATCH_SIZE, parsed))
}

function computeFacets(books) {
    const counts = new Map()
    for (const book of books) {
        const codes = Array.isArray(book.genreCodes) && book.genreCodes.length ? book.genreCodes : [NO_GENRE_CODE]
        for (const code of codes) {
            counts.set(code, (counts.get(code) ?? 0) + 1)
        }
    }
    return Array.from(counts.entries()).map(([genre, count]) => ({ genre, count }))
}

function searchBooks(term, page, pageSize, genres) {
    if (!index) return { books: [], total: 0, genres: [] }
    const hasTerm = term.trim().length > 0
    const genreFilter = genres && genres.length ? genres : null

    // Text search — BM25-ranked, AND across all tokens, prefix matching
    let termMatched
    if (!hasTerm) {
        termMatched = Array.from(booksById.values())
    } else {
        const results = index.search(term, SEARCH_OPTIONS)
        termMatched = results.map((r) => booksById.get(String(r.id))).filter(Boolean)
    }

    // Stable genre facets from the pre-filter result set
    const resultGenres = computeFacets(termMatched)

    // Genre filter — JS post-filter (OR across selected genres)
    let matched
    if (!genreFilter) {
        matched = termMatched
    } else {
        matched = termMatched.filter(
            (book) => Array.isArray(book.genreCodes) && book.genreCodes.some((c) => genreFilter.includes(c)),
        )
    }

    const total = matched.length
    const start = (page - 1) * pageSize
    return { books: matched.slice(start, start + pageSize), total, genres: resultGenres }
}

self.onmessage = async (event) => {
    const message = event?.data ?? {}

    if (message.type === "restore") {
        const hash = typeof message.hash === "string" ? message.hash : ""
        const signature = typeof message.signature === "string" ? message.signature : ""

        if (!hash) {
            self.postMessage({ type: "restore-complete", payload: { restored: false, reason: "invalid" } })
            return
        }

        try {
            const persisted = (await readPersistedIndex(hash)) as {
                signature: string
                indexJson: string
                total: number
                metadata: unknown
                updatedAt: string
            } | null
            if (!persisted) {
                self.postMessage({ type: "restore-complete", payload: { restored: false, reason: "missing" } })
                return
            }

            if (signature && persisted.signature !== signature) {
                self.postMessage({ type: "restore-complete", payload: { restored: false, reason: "stale" } })
                return
            }

            if (!persisted.indexJson) {
                self.postMessage({ type: "restore-complete", payload: { restored: false, reason: "missing-index" } })
                return
            }

            const persistedBooks = (await readPersistedLibraryBooks(hash)) as { id: unknown }[]
            if (!persistedBooks.length) {
                self.postMessage({ type: "restore-complete", payload: { restored: false, reason: "missing-books" } })
                return
            }

            const restoredIndex = MiniSearch.loadJSON(persisted.indexJson, INDEX_OPTIONS)
            index = restoredIndex
            booksById = new Map(persistedBooks.map((book) => [String(book.id), book]))
            activeHash = hash

            self.postMessage({
                type: "restore-complete",
                payload: {
                    restored: true,
                    total: persisted.total ?? persistedBooks.length,
                    persistedAt: persisted.updatedAt ?? "",
                    metadata: persisted.metadata ?? null,
                },
            })
        } catch (err) {
            self.postMessage({
                type: "restore-complete",
                payload: {
                    restored: false,
                    reason: "error",
                    message: err instanceof Error ? err.message : "Failed to restore search index.",
                },
            })
        }

        return
    }

    if (message.type === "clear-persisted") {
        const hash = typeof message.hash === "string" ? message.hash : ""

        if (!hash) {
            self.postMessage({ type: "clear-persisted-complete", payload: { cleared: false, reason: "invalid" } })
            return
        }

        try {
            await Promise.all([deletePersistedIndex(hash), deletePersistedLibraryBooks(hash)])

            if (activeHash === hash) {
                resetInMemoryIndex()
            }

            self.postMessage({ type: "clear-persisted-complete", payload: { cleared: true } })
        } catch (err) {
            self.postMessage({
                type: "clear-persisted-complete",
                payload: {
                    cleared: false,
                    reason: "error",
                    message: err instanceof Error ? err.message : "Failed to clear persisted index.",
                },
            })
        }

        return
    }

    if (message.type === "parse-and-build") {
        const buffer = message.buffer
        const hash = typeof message.hash === "string" ? message.hash : ""
        const batchSize = resolveBatchSize(message.batchSize)

        index = createIndex()
        booksById = new Map()
        activeHash = hash

        if (!buffer) {
            self.postMessage({
                type: "build-complete",
                payload: { total: 0, persisted: false, persistenceError: "No buffer provided." },
            })
            return
        }

        try {
            // Phase 1: Parse INPX
            const { metadata, books, datasetSignature } = parseInpxBuffer(
                buffer,
                (phase, processed, total, percent) => {
                    self.postMessage({ type: "build-progress", payload: { phase, processed, total, percent } })
                },
            )

            // Phase 2: Build MiniSearch index in batches (yields between each batch)
            const newIndex = createIndex()
            const total = books.length
            const seenIds = new Set()

            for (let start = 0; start < total; start += batchSize) {
                const end = Math.min(start + batchSize, total)

                for (let i = start; i < end; i++) {
                    const book = books[i]
                    const id = String(book.id)
                    if (seenIds.has(id)) continue
                    seenIds.add(id)
                    booksById.set(id, book)
                    newIndex.add(toIndexDoc(book))
                }

                self.postMessage({
                    type: "build-progress",
                    payload: {
                        phase: "indexing",
                        processed: end,
                        total,
                        percent: Math.round((end / total) * 100),
                    },
                })

                // Yield control between batches
                await new Promise((resolve) => setTimeout(resolve, 0))
            }

            index = newIndex

            // Phase 3: Serialise and persist to IDB
            const indexJson = JSON.stringify(index.toJSON())
            let persisted = false
            let persistenceError = ""

            if (hash) {
                try {
                    await Promise.all([
                        writePersistedIndex(hash, datasetSignature, indexJson, total, metadata),
                        writePersistedLibraryBooks(hash, books),
                    ])
                    persisted = true
                } catch (err) {
                    persistenceError = err instanceof Error ? err.message : "Failed to persist index."
                }
            }

            self.postMessage({
                type: "build-complete",
                payload: { total, metadata, datasetSignature, persisted, persistenceError },
            })
        } catch (err) {
            resetInMemoryIndex()
            self.postMessage({
                type: "build-error",
                message: err instanceof Error ? err.message : "Failed to parse and build index.",
            })
        }

        return
    }

    if (message.type === "search") {
        const { requestId, term, page, pageSize, genres } = message
        const result = searchBooks(
            typeof term === "string" ? term : "",
            typeof page === "number" ? page : 1,
            typeof pageSize === "number" ? pageSize : 30,
            Array.isArray(genres) ? genres : [],
        )
        self.postMessage({ type: "search-result", requestId, payload: result })
        return
    }
}
