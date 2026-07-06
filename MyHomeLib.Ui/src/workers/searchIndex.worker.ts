import { parseInpxBufferStreaming } from "inpx-parser"
import MiniSearch, { type AsPlainObject } from "minisearch"

const DEFAULT_INDEX_BATCH_SIZE = 4096
const MIN_INDEX_BATCH_SIZE = 1000
const MAX_INDEX_BATCH_SIZE = 10000
// Bump when the persisted index/facets layout changes: older cache records
// are treated as stale and the index is rebuilt from the INPX download.
const PERSISTENCE_FORMAT_VERSION = 2
const PERSISTENCE_META_DB_NAME = "myhomelib-search-index-meta-cache"
const PERSISTENCE_META_STORE_NAME = "indexes"
const PERSISTENCE_META_DB_VERSION = 1
const PERSISTENCE_JSON_DB_NAME = "myhomelib-search-index-json-cache"
const PERSISTENCE_JSON_STORE_NAME = "indexes"
const PERSISTENCE_JSON_DB_VERSION = 1
const INDEX_PAYLOAD_ENCODING_GZIP_JSON = "gzip-json"
const INDEX_PAYLOAD_ENCODING_JS_OBJECT = "js-object"
const LIBRARY_CACHE_DB_PREFIX = "myhomelib-library-books"
const LIBRARY_CACHE_HASH_PREFIX_LENGTH = 12
const LIBRARY_CACHE_BOOKS_STORE_NAME = "books"
const LIBRARY_CACHE_DB_VERSION = 1

let index = null // MiniSearch instance
let facetDataById = new Map() // Lightweight: { id, genreCodes, year }
let activeHash = ""
let persistenceMetaDbPromise = null
let persistenceJsonDbPromise = null
const libraryCacheDbPromises = new Map()

type PersistedMetaRecord = {
    hash: string
    signature: string
    formatVersion?: number
    total: number
    metadata: unknown
    updatedAt: string
}

type PersistedIndexJsRecord = {
    hash: string
    encoding: string
    indexPayload: unknown
    facetsEncoding?: string
    facetsPayload?: unknown
    updatedAt: string
}

type PersistedIndexRecord = PersistedMetaRecord & {
    encoding: string
    indexPayload: unknown
    facetsEncoding?: string
    facetsPayload?: unknown
}

function toUint8Array(value: unknown): Uint8Array | null {
    if (value instanceof Uint8Array) return value
    if (value instanceof ArrayBuffer) return new Uint8Array(value)
    return null
}

function toArrayBuffer(value: Uint8Array): ArrayBuffer {
    const start = value.byteOffset
    const end = value.byteOffset + value.byteLength
    return value.buffer.slice(start, end) as ArrayBuffer
}

async function encodeJsonPayload(value: unknown): Promise<{ encoding: string; payload: unknown }> {
    if (typeof CompressionStream !== "function") {
        return { encoding: INDEX_PAYLOAD_ENCODING_JS_OBJECT, payload: value }
    }

    const jsonText = JSON.stringify(value)
    const rawBytes = new TextEncoder().encode(jsonText)
    const stream = new Blob([rawBytes]).stream().pipeThrough(new CompressionStream("gzip"))
    const compressedBuffer = await new Response(stream).arrayBuffer()
    const compressedBytes = new Uint8Array(compressedBuffer)

    // Keep raw JS object when compression ratio is poor to avoid CPU overhead on restore.
    if (compressedBytes.byteLength >= rawBytes.byteLength * 0.95) {
        return { encoding: INDEX_PAYLOAD_ENCODING_JS_OBJECT, payload: value }
    }

    return { encoding: INDEX_PAYLOAD_ENCODING_GZIP_JSON, payload: compressedBytes }
}

async function decodeJsonPayload(encoding: string, payload: unknown): Promise<unknown> {
    if (encoding === INDEX_PAYLOAD_ENCODING_JS_OBJECT) {
        return payload ?? null
    }

    if (encoding !== INDEX_PAYLOAD_ENCODING_GZIP_JSON) {
        return null
    }

    if (typeof DecompressionStream !== "function") {
        return null
    }

    const bytes = toUint8Array(payload)
    if (!bytes || bytes.byteLength === 0) {
        return null
    }

    const stream = new Blob([toArrayBuffer(bytes)]).stream().pipeThrough(new DecompressionStream("gzip"))
    const decompressedBuffer = await new Response(stream).arrayBuffer()
    const jsonText = new TextDecoder().decode(new Uint8Array(decompressedBuffer))
    return JSON.parse(jsonText)
}

// Normalise a term: Cyrillic-aware lowercase + collapse e/yo.
// Returning null causes MiniSearch to skip the token.
const processTerm = (term) => {
    const normalized = term.toLocaleLowerCase("ru-RU").replace(/ё/g, "е")
    return normalized.length > 0 ? normalized : null
}

const INDEX_OPTIONS = {
    fields: ["title", "authors", "series", "lang"],
    idField: "id",
    storeFields: [],
    processTerm,
}

// AND: every query token must appear in the document.
// BM25 then ranks documents that match more / higher-boost fields first.
const SEARCH_OPTIONS = {
    prefix: true,
    combineWith: "AND",
    boost: { title: 4, authors: 2, series: 1.5, lang: 1 },
}

const NO_GENRE_CODE = "__no_genre__"

function toIndexDoc(book) {
    return {
        id: Number(book.id),
        title: String(book.title ?? ""),
        authors: String(book.authors ?? ""),
        series: String(book.series ?? ""),
        lang: String(book.lang ?? ""),
    }
}

// Parsed once at build/restore time so searches never touch date strings.
function parseYearFromDate(date): number | null {
    if (typeof date !== "string" || date.length < 4) return null
    const year = Number.parseInt(date.slice(0, 4), 10)
    return Number.isNaN(year) ? null : year
}

function toFacetData(book) {
    return {
        id: Number(book.id),
        genreCodes: Array.isArray(book.genreCodes) ? book.genreCodes : [],
        year: parseYearFromDate(book.date),
    }
}

function createIndex() {
    return new MiniSearch(INDEX_OPTIONS)
}

// --- Query-path caches ---------------------------------------------------
// Search results are recomputed per message; without these, every page flip
// re-copies and re-scans the full facet set (O(n) per click at 100k+ books).
let cachedAllFacets = null // Array.from(facetDataById.values()), built lazily
let lastTermCache = null // { term, termMatched, yearRange }
let lastQueryCache = null // { key, matched, resultGenres }

function invalidateSearchCaches() {
    cachedAllFacets = null
    lastTermCache = null
    lastQueryCache = null
}

function getAllFacets() {
    if (!cachedAllFacets) {
        cachedAllFacets = Array.from(facetDataById.values())
    }
    return cachedAllFacets
}

function openPersistenceMetaDb() {
    if (persistenceMetaDbPromise) {
        return persistenceMetaDbPromise
    }

    persistenceMetaDbPromise = new Promise((resolve, reject) => {
        const request = indexedDB.open(PERSISTENCE_META_DB_NAME, PERSISTENCE_META_DB_VERSION)

        request.onupgradeneeded = () => {
            const db = request.result
            if (!db.objectStoreNames.contains(PERSISTENCE_META_STORE_NAME)) {
                db.createObjectStore(PERSISTENCE_META_STORE_NAME, { keyPath: "hash" })
            }
        }

        request.onsuccess = () => resolve(request.result)
        request.onerror = () => reject(request.error ?? new Error("Failed to open search metadata DB."))
    })

    return persistenceMetaDbPromise
}

function openPersistenceJsonDb() {
    if (persistenceJsonDbPromise) {
        return persistenceJsonDbPromise
    }

    persistenceJsonDbPromise = new Promise((resolve, reject) => {
        const request = indexedDB.open(PERSISTENCE_JSON_DB_NAME, PERSISTENCE_JSON_DB_VERSION)

        request.onupgradeneeded = () => {
            const db = request.result
            if (!db.objectStoreNames.contains(PERSISTENCE_JSON_STORE_NAME)) {
                db.createObjectStore(PERSISTENCE_JSON_STORE_NAME, { keyPath: "hash" })
            }
        }

        request.onsuccess = () => resolve(request.result)
        request.onerror = () => reject(request.error ?? new Error("Failed to open search index JSON DB."))
    })

    return persistenceJsonDbPromise
}

async function readPersistedIndex(hash) {
    const [metaDb, jsonDb] = await Promise.all([openPersistenceMetaDb(), openPersistenceJsonDb()])

    const meta = await new Promise<PersistedMetaRecord | null>((resolve, reject) => {
        const tx = metaDb.transaction(PERSISTENCE_META_STORE_NAME, "readonly")
        const request = tx.objectStore(PERSISTENCE_META_STORE_NAME).get(hash)

        request.onsuccess = () => resolve(request.result ?? null)
        request.onerror = () => reject(request.error ?? new Error("Failed to read persisted search metadata."))
    })

    const indexEntry = await new Promise<PersistedIndexJsRecord | null>((resolve, reject) => {
        const tx = jsonDb.transaction(PERSISTENCE_JSON_STORE_NAME, "readonly")
        const request = tx.objectStore(PERSISTENCE_JSON_STORE_NAME).get(hash)

        request.onsuccess = () => resolve(request.result ?? null)
        request.onerror = () => reject(request.error ?? new Error("Failed to read persisted search index JSON."))
    })

    if (!meta || !indexEntry) {
        return null
    }

    return {
        ...meta,
        encoding: indexEntry.encoding ?? INDEX_PAYLOAD_ENCODING_JS_OBJECT,
        indexPayload: indexEntry.indexPayload ?? null,
        facetsEncoding: indexEntry.facetsEncoding ?? INDEX_PAYLOAD_ENCODING_JS_OBJECT,
        facetsPayload: indexEntry.facetsPayload ?? null,
    }
}

async function writePersistedIndex(hash, signature, indexJs, facets, total, metadata) {
    const [metaDb, jsonDb] = await Promise.all([openPersistenceMetaDb(), openPersistenceJsonDb()])
    const updatedAt = new Date().toISOString()
    const encodedIndex = await encodeJsonPayload(indexJs)
    // Compact facet array persisted alongside the index: restore reads one
    // record instead of walking the full books store.
    const encodedFacets = await encodeJsonPayload(facets)

    await new Promise((resolve, reject) => {
        const tx = metaDb.transaction(PERSISTENCE_META_STORE_NAME, "readwrite")
        tx.objectStore(PERSISTENCE_META_STORE_NAME).put({
            hash,
            signature,
            formatVersion: PERSISTENCE_FORMAT_VERSION,
            total,
            metadata: metadata ?? null,
            updatedAt,
        })

        tx.oncomplete = () => resolve(undefined)
        tx.onerror = () => reject(tx.error ?? new Error("Failed to persist search metadata."))
    })

    await new Promise((resolve, reject) => {
        const tx = jsonDb.transaction(PERSISTENCE_JSON_STORE_NAME, "readwrite")
        tx.objectStore(PERSISTENCE_JSON_STORE_NAME).put({
            hash,
            encoding: encodedIndex.encoding,
            indexPayload: encodedIndex.payload,
            facetsEncoding: encodedFacets.encoding,
            facetsPayload: encodedFacets.payload,
            updatedAt,
        })

        tx.oncomplete = () => resolve(undefined)
        tx.onerror = () => reject(tx.error ?? new Error("Failed to persist search index JSON."))
    })
}

function getLibraryCacheDbName(hash) {
    const normalizedHash = String(hash ?? "")
        .toLowerCase()
        .replace(/[^a-z0-9]/g, "")
    const token = normalizedHash.slice(0, LIBRARY_CACHE_HASH_PREFIX_LENGTH)

    if (!token) {
        return `${LIBRARY_CACHE_DB_PREFIX}-session`
    }

    return `${LIBRARY_CACHE_DB_PREFIX}-${token}`
}

function openLibraryCacheDb(hash) {
    const dbName = getLibraryCacheDbName(hash)
    if (libraryCacheDbPromises.has(dbName)) {
        return libraryCacheDbPromises.get(dbName)
    }

    const dbPromise = new Promise((resolve, reject) => {
        const request = indexedDB.open(dbName, LIBRARY_CACHE_DB_VERSION)

        request.onupgradeneeded = () => {
            const db = request.result
            if (!db.objectStoreNames.contains(LIBRARY_CACHE_BOOKS_STORE_NAME)) {
                db.createObjectStore(LIBRARY_CACHE_BOOKS_STORE_NAME, { keyPath: "id" })
            }
        }

        request.onsuccess = () => resolve(request.result)
        request.onerror = () => reject(request.error ?? new Error("Failed to open library cache DB."))
    })

    libraryCacheDbPromises.set(dbName, dbPromise)
    return dbPromise
}

// Fallback path when the persisted facets payload is missing: bulk-read the
// books store (getAll is far faster than a per-row cursor) and derive facets.
async function readPersistedLibraryFacets(hash): Promise<{ id: number; genreCodes: string[]; year: number | null }[]> {
    const db = await openLibraryCacheDb(hash)

    return new Promise((resolve, reject) => {
        const tx = db.transaction(LIBRARY_CACHE_BOOKS_STORE_NAME, "readonly")
        const request = tx.objectStore(LIBRARY_CACHE_BOOKS_STORE_NAME).getAll()

        request.onsuccess = () => {
            const rows = request.result ?? []
            resolve(rows.map((row) => toFacetData(row)))
        }
        request.onerror = () => reject(request.error ?? new Error("Failed to read library facets from cache DB."))
    })
}

async function fetchBooksByIds(hash, ids) {
    if (!ids.length) return []
    const db = await openLibraryCacheDb(hash)

    return new Promise((resolve) => {
        const tx = db.transaction(LIBRARY_CACHE_BOOKS_STORE_NAME, "readonly")
        const store = tx.objectStore(LIBRARY_CACHE_BOOKS_STORE_NAME)
        const books = []
        let remaining = ids.length

        for (const id of ids) {
            const request = store.get(id)
            request.onsuccess = () => {
                if (request.result) {
                    books.push(request.result)
                }
                remaining--
                if (remaining === 0) {
                    resolve(books)
                }
            }
            request.onerror = () => {
                remaining--
                if (remaining === 0) {
                    resolve(books)
                }
            }
        }
    })
}

async function writePersistedLibraryBooksBatch(hash, rows): Promise<void> {
    const db = await openLibraryCacheDb(hash)

    return new Promise<void>((resolve, reject) => {
        const tx = db.transaction(LIBRARY_CACHE_BOOKS_STORE_NAME, "readwrite")
        const store = tx.objectStore(LIBRARY_CACHE_BOOKS_STORE_NAME)
        for (const book of rows) {
            store.put(book)
        }

        tx.oncomplete = () => resolve(undefined)
        tx.onerror = () => reject(tx.error ?? new Error("Failed to write library books to cache."))
    })
}

async function deletePersistedLibraryBooks(hash) {
    const db = await openLibraryCacheDb(hash)

    return new Promise((resolve, reject) => {
        const tx = db.transaction(LIBRARY_CACHE_BOOKS_STORE_NAME, "readwrite")
        tx.objectStore(LIBRARY_CACHE_BOOKS_STORE_NAME).clear()

        tx.oncomplete = () => resolve(undefined)
        tx.onerror = () => reject(tx.error ?? new Error("Failed to delete library books from cache."))
    })
}

async function deleteLibraryCacheDb(hash) {
    const dbName = getLibraryCacheDbName(hash)
    const existing = libraryCacheDbPromises.get(dbName)
    if (existing) {
        try {
            const db = await existing
            db.close()
        } catch {
            // ignored
        }
        libraryCacheDbPromises.delete(dbName)
    }

    return new Promise((resolve, reject) => {
        const request = indexedDB.deleteDatabase(dbName)
        request.onsuccess = () => resolve(undefined)
        request.onerror = () => reject(request.error ?? new Error("Failed to delete library cache DB."))
        request.onblocked = () => reject(new Error("Deleting library cache DB was blocked."))
    })
}

function deleteDbByName(dbName) {
    const known = libraryCacheDbPromises.get(dbName)
    if (known) {
        known
            .then((db) => db.close())
            .catch(() => {
                // ignored
            })
        libraryCacheDbPromises.delete(dbName)
    }

    return new Promise((resolve, reject) => {
        const request = indexedDB.deleteDatabase(dbName)
        request.onsuccess = () => resolve(undefined)
        request.onerror = () => reject(request.error ?? new Error(`Failed to delete DB: ${dbName}`))
        request.onblocked = () => reject(new Error(`Deleting DB was blocked: ${dbName}`))
    })
}

async function clearAllPersistedData() {
    const dbNames = new Set([
        PERSISTENCE_META_DB_NAME,
        PERSISTENCE_JSON_DB_NAME,
        ...Array.from(libraryCacheDbPromises.keys()),
    ])

    if (typeof indexedDB.databases === "function") {
        try {
            const dbs = await indexedDB.databases()
            for (const db of dbs) {
                const name = db?.name ?? ""
                if (!name) continue
                if (name === PERSISTENCE_META_DB_NAME || name === PERSISTENCE_JSON_DB_NAME) {
                    dbNames.add(name)
                    continue
                }
                if (name.startsWith(`${LIBRARY_CACHE_DB_PREFIX}-`)) {
                    dbNames.add(name)
                }
            }
        } catch {
            // ignored: fallback to known DB names only
        }
    }

    const deletions = Array.from(dbNames.values()).map((name) => deleteDbByName(name))
    await Promise.all(deletions)
    resetInMemoryIndex()
}

async function deletePersistedIndex(hash) {
    const [metaDb, jsonDb] = await Promise.all([openPersistenceMetaDb(), openPersistenceJsonDb()])

    await new Promise((resolve, reject) => {
        const tx = metaDb.transaction(PERSISTENCE_META_STORE_NAME, "readwrite")
        tx.objectStore(PERSISTENCE_META_STORE_NAME).delete(hash)

        tx.oncomplete = () => resolve(undefined)
        tx.onerror = () => reject(tx.error ?? new Error("Failed to clear persisted search metadata."))
    })

    await new Promise((resolve, reject) => {
        const tx = jsonDb.transaction(PERSISTENCE_JSON_STORE_NAME, "readwrite")
        tx.objectStore(PERSISTENCE_JSON_STORE_NAME).delete(hash)

        tx.oncomplete = () => resolve(undefined)
        tx.onerror = () => reject(tx.error ?? new Error("Failed to clear persisted search index JSON."))
    })
}

function resetInMemoryIndex() {
    index = null
    facetDataById = new Map()
    activeHash = ""
    invalidateSearchCaches()
}

function resolveBatchSize(value) {
    const parsed = Number.parseInt(value, 10)

    if (!Number.isFinite(parsed)) {
        return DEFAULT_INDEX_BATCH_SIZE
    }

    return Math.min(MAX_INDEX_BATCH_SIZE, Math.max(MIN_INDEX_BATCH_SIZE, parsed))
}

function computeFacets(facets) {
    const counts = new Map()
    for (const facet of facets) {
        const codes = Array.isArray(facet.genreCodes) && facet.genreCodes.length ? facet.genreCodes : [NO_GENRE_CODE]
        for (const code of codes) {
            counts.set(code, (counts.get(code) ?? 0) + 1)
        }
    }
    return Array.from(counts.entries()).map(([genre, count]) => ({ genre, count }))
}

async function searchBooks(term, page, pageSize, genres, yearFrom, yearTo) {
    if (!index) return { books: [], total: 0, genres: [], yearRange: null }
    const normalizedTerm = term.trim()
    const hasTerm = normalizedTerm.length > 0
    const genreFilter = genres && genres.length ? genres : null
    const hasYearFrom = typeof yearFrom === "number" && Number.isFinite(yearFrom)
    const hasYearTo = typeof yearTo === "number" && Number.isFinite(yearTo)

    // Term-level cache: the matched set and year range depend on the term
    // only, so genre/year filter changes reuse them.
    let termMatched
    let yearRange
    if (lastTermCache && lastTermCache.term === normalizedTerm) {
        termMatched = lastTermCache.termMatched
        yearRange = lastTermCache.yearRange
    } else {
        // Text search — BM25-ranked, AND across all tokens, prefix matching
        if (!hasTerm) {
            termMatched = getAllFacets()
        } else {
            const results = index.search(normalizedTerm, SEARCH_OPTIONS)
            termMatched = results.map((r) => facetDataById.get(r.id)).filter(Boolean)
        }

        // Compute year range from the full term-matched set (for slider bounds)
        let minYear = Infinity
        let maxYear = -Infinity
        for (const facet of termMatched) {
            const y = facet.year
            if (y !== null && y > 1000) {
                if (y < minYear) minYear = y
                if (y > maxYear) maxYear = y
            }
        }
        yearRange = minYear <= maxYear ? { min: minYear, max: maxYear } : null

        lastTermCache = { term: normalizedTerm, termMatched, yearRange }
    }

    // Query-level cache: filtered set + genre facets. Page flips hit this and
    // reduce to a slice + page-sized IndexedDB fetch.
    const queryKey = JSON.stringify([
        normalizedTerm,
        genreFilter,
        hasYearFrom ? yearFrom : null,
        hasYearTo ? yearTo : null,
    ])

    let matched
    let resultGenres
    if (lastQueryCache && lastQueryCache.key === queryKey) {
        matched = lastQueryCache.matched
        resultGenres = lastQueryCache.resultGenres
    } else {
        // Year filter — applied before genre facets so counts reflect the selected range
        let yearFiltered
        if (hasYearFrom || hasYearTo) {
            yearFiltered = termMatched.filter((facet) => {
                if (facet.year === null) return false
                if (hasYearFrom && facet.year < yearFrom) return false
                if (hasYearTo && facet.year > yearTo) return false
                return true
            })
        } else {
            yearFiltered = termMatched
        }

        // Genre facets from the year-filtered set (stable across genre selection)
        resultGenres = computeFacets(yearFiltered)

        // Genre filter — JS post-filter (OR across selected genres)
        if (!genreFilter) {
            matched = yearFiltered
        } else {
            matched = yearFiltered.filter(
                (facet) => Array.isArray(facet.genreCodes) && facet.genreCodes.some((c) => genreFilter.includes(c)),
            )
        }

        lastQueryCache = { key: queryKey, matched, resultGenres }
    }

    const total = matched.length
    const start = (page - 1) * pageSize
    const pageIds = matched.slice(start, start + pageSize).map((f) => f.id)

    // Fetch only the books needed for this page from IndexedDB
    const books = (await fetchBooksByIds(activeHash, pageIds)) as Array<{
        id: number
        title: string
        authors: string
        genreCodes: string[]
        [key: string]: unknown
    }>

    // Sort books to match the order of pageIds (maintain BM25 relevance order)
    const booksMap = new Map(books.map((b) => [b.id, b]))
    const orderedBooks = pageIds.map((id) => booksMap.get(id)).filter(Boolean)

    return { books: orderedBooks, total, genres: resultGenres, yearRange }
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
            const persisted = (await readPersistedIndex(hash)) as PersistedIndexRecord | null
            if (!persisted) {
                self.postMessage({ type: "restore-complete", payload: { restored: false, reason: "missing" } })
                return
            }

            if (signature && persisted.signature !== signature) {
                self.postMessage({ type: "restore-complete", payload: { restored: false, reason: "stale" } })
                return
            }

            if ((persisted.formatVersion ?? 1) !== PERSISTENCE_FORMAT_VERSION) {
                self.postMessage({ type: "restore-complete", payload: { restored: false, reason: "stale" } })
                return
            }

            // Emit metadata immediately so UI can render header details while heavy restore continues.
            self.postMessage({
                type: "restore-metadata",
                payload: {
                    hash,
                    metadata: persisted.metadata ?? null,
                    total: persisted.total ?? 0,
                    persistedAt: persisted.updatedAt ?? "",
                },
            })

            if (!persisted.indexPayload) {
                self.postMessage({ type: "restore-complete", payload: { restored: false, reason: "missing-index" } })
                return
            }

            const indexJs = (await decodeJsonPayload(
                persisted.encoding,
                persisted.indexPayload,
            )) as AsPlainObject | null
            if (!indexJs) {
                self.postMessage({ type: "restore-complete", payload: { restored: false, reason: "missing-index" } })
                return
            }

            // Prefer the compact persisted facets record; fall back to
            // deriving facets from the full books store.
            let persistedFacets = (await decodeJsonPayload(
                persisted.facetsEncoding ?? INDEX_PAYLOAD_ENCODING_JS_OBJECT,
                persisted.facetsPayload,
            )) as { id: number; genreCodes: string[]; year: number | null }[] | null
            if (!Array.isArray(persistedFacets) || !persistedFacets.length) {
                persistedFacets = await readPersistedLibraryFacets(hash)
            }
            if (!persistedFacets.length) {
                self.postMessage({ type: "restore-complete", payload: { restored: false, reason: "missing-books" } })
                return
            }

            const restoredIndex = MiniSearch.loadJS(indexJs, INDEX_OPTIONS)
            index = restoredIndex
            facetDataById = new Map(persistedFacets.map((facet) => [facet.id, facet]))
            activeHash = hash
            invalidateSearchCaches()

            self.postMessage({
                type: "restore-complete",
                payload: {
                    restored: true,
                    total: persisted.total ?? persistedFacets.length,
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
            await Promise.all([deletePersistedIndex(hash), deleteLibraryCacheDb(hash)])

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

    if (message.type === "clear-all-persisted") {
        try {
            await clearAllPersistedData()
            self.postMessage({ type: "clear-all-persisted-complete", payload: { cleared: true } })
        } catch (err) {
            self.postMessage({
                type: "clear-all-persisted-complete",
                payload: {
                    cleared: false,
                    reason: "error",
                    message: err instanceof Error ? err.message : "Failed to clear all persisted data.",
                },
            })
        }

        return
    }

    if (message.type === "parse-and-build") {
        const buffer = message.buffer
        const hash = typeof message.hash === "string" ? message.hash : ""
        const batchSize = resolveBatchSize(message.batchSize)
        const targetHash = hash || `session-${Date.now()}-${Math.random().toString(36).slice(2)}`
        const shouldPersist = Boolean(hash)

        const newIndex = createIndex()
        const seenIds = new Set<number>()
        let processed = 0

        index = newIndex
        facetDataById = new Map()
        activeHash = shouldPersist ? targetHash : ""

        if (!buffer) {
            self.postMessage({
                type: "build-complete",
                payload: { total: 0, persisted: false, persistenceError: "No buffer provided." },
            })
            return
        }

        try {
            await deletePersistedLibraryBooks(targetHash)

            // Parse, persist, and index each batch in one pass. The IndexedDB
            // write runs while the batch is indexed, so per-batch cost is
            // max(IO, CPU) rather than their sum.
            const { metadata, datasetSignature } = await parseInpxBufferStreaming(
                buffer,
                async (booksBatch) => {
                    const writePromise = writePersistedLibraryBooksBatch(targetHash, booksBatch)

                    for (const book of booksBatch) {
                        const id = Number(book.id)
                        if (seenIds.has(id)) continue
                        seenIds.add(id)
                        facetDataById.set(id, toFacetData(book))
                        newIndex.add(toIndexDoc(book))
                    }

                    processed += booksBatch.length
                    invalidateSearchCaches()
                    await writePromise
                },
                (phase, parsedProcessed, parsedTotal, percent) => {
                    self.postMessage({
                        type: "build-progress",
                        payload: { phase, processed: parsedProcessed, total: parsedTotal, percent },
                    })
                },
                batchSize,
            )

            const total = metadata?.totalBooks ?? processed

            self.postMessage({
                type: "build-progress",
                payload: {
                    phase: "indexing",
                    processed: total,
                    total,
                    percent: 100,
                },
            })

            index = newIndex
            invalidateSearchCaches()

            const indexJs = index.toJSON()
            let persisted = false
            let persistenceError = ""

            if (shouldPersist) {
                try {
                    await writePersistedIndex(targetHash, datasetSignature, indexJs, getAllFacets(), total, metadata)
                    persisted = true
                } catch (err) {
                    persistenceError = err instanceof Error ? err.message : "Failed to persist index."
                }
            }

            if (!shouldPersist) {
                try {
                    await deletePersistedLibraryBooks(targetHash)
                } catch {
                    // no-op: temporary cache cleanup failed
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
        const { requestId, term, page, pageSize, genres, yearFrom, yearTo } = message
        const result = await searchBooks(
            typeof term === "string" ? term : "",
            typeof page === "number" ? page : 1,
            typeof pageSize === "number" ? pageSize : 30,
            Array.isArray(genres) ? genres : [],
            typeof yearFrom === "number" ? yearFrom : undefined,
            typeof yearTo === "number" ? yearTo : undefined,
        )
        self.postMessage({ type: "search-result", requestId, payload: result })
        return
    }
}
