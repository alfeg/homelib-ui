import { Document as FlexDocument } from "flexsearch"

import { parseInpxBuffer } from "./inpxParser"

const DEFAULT_INDEX_BATCH_SIZE = 1024
const MIN_INDEX_BATCH_SIZE = 500
const MAX_INDEX_BATCH_SIZE = 2000
const BATCH_YIELD_DELAY_MS = 0
const PERSISTENCE_DB_NAME = "myhomelib-search-index-cache"
const PERSISTENCE_STORE_NAME = "indexes"
const PERSISTENCE_DB_VERSION = 2
const LIBRARY_CACHE_DB_NAME = "myhomelib-library-cache"
const LIBRARY_CACHE_STORE_NAME = "libraries"
const LIBRARY_CACHE_DB_VERSION = 1

let index = null
let booksById = new Map()
let activeHash = ""
let persistenceDbPromise = null
let libraryCacheDbPromise = null

function normalizeSearchValue(value) {
    return String(value ?? "")
        .toLocaleLowerCase("ru-RU")
        .replaceAll("ё", "е")
}

function createIndex() {
    return new FlexDocument({
        cache: true,
        document: {
            id: "id",
            index: [
                {
                    field: "content",
                    tokenize: "forward",
                    encode: false,
                },
            ],
        },
    })
}

function openPersistenceDb() {
    if (persistenceDbPromise) {
        return persistenceDbPromise
    }

    persistenceDbPromise = new Promise((resolve, reject) => {
        const request = indexedDB.open(PERSISTENCE_DB_NAME, PERSISTENCE_DB_VERSION)

        request.onupgradeneeded = () => {
            const db = request.result
            if (!db.objectStoreNames.contains(PERSISTENCE_STORE_NAME)) {
                db.createObjectStore(PERSISTENCE_STORE_NAME, { keyPath: "hash" })
            }
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

async function writePersistedIndex(hash, signature, chunks, total, metadata) {
    const db = await openPersistenceDb()

    return new Promise((resolve, reject) => {
        const tx = db.transaction(PERSISTENCE_STORE_NAME, "readwrite")
        tx.objectStore(PERSISTENCE_STORE_NAME).put({
            hash,
            signature,
            chunks,
            total,
            metadata: metadata ?? null,
            updatedAt: new Date().toISOString(),
        })

        tx.oncomplete = () => resolve()
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

        tx.oncomplete = () => resolve()
        tx.onerror = () => reject(tx.error ?? new Error("Failed to clear persisted search index."))
    })
}

async function exportIndex(targetIndex) {
    const chunks = []

    await targetIndex.export((key, data) => {
        if (key == null) return
        chunks.push({ key, data })
    })

    return chunks
}

async function importIndex(targetIndex, chunks) {
    const source = Array.isArray(chunks) ? chunks : []

    for (let i = 0; i < source.length; i += 1) {
        const chunk = source[i]
        if (!chunk || chunk.key == null) continue
        await targetIndex.import(chunk.key, chunk.data)
    }
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

async function addBatchToIndexAsync(targetIndex, documents) {
    if (typeof targetIndex?.addAsync === "function") {
        await targetIndex.addAsync(documents)
        return
    }

    targetIndex.add(documents)
}

self.onmessage = async (event) => {
    const message = event?.data ?? {}

    if (message.type === "restore") {
        const hash = typeof message.hash === "string" ? message.hash : ""
        const signature = typeof message.signature === "string" ? message.signature : ""

        if (!hash) {
            self.postMessage({
                type: "restore-complete",
                payload: {
                    restored: false,
                    reason: "invalid",
                },
            })
            return
        }

        try {
            const persisted = await readPersistedIndex(hash)
            if (!persisted) {
                self.postMessage({
                    type: "restore-complete",
                    payload: {
                        restored: false,
                        reason: "missing",
                    },
                })
                return
            }

            if (signature && persisted.signature !== signature) {
                self.postMessage({
                    type: "restore-complete",
                    payload: {
                        restored: false,
                        reason: "stale",
                    },
                })
                return
            }

            const restoredIndex = createIndex()
            await importIndex(restoredIndex, persisted.chunks)
            const persistedBooks = await readPersistedLibraryBooks(hash)
            if (!persistedBooks.length) {
                self.postMessage({
                    type: "restore-complete",
                    payload: {
                        restored: false,
                        reason: "missing-books",
                    },
                })
                return
            }

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

            // Transfer index + books to main thread for local search
            self.postMessage({ type: "index-data", payload: { chunks: persisted.chunks, books: persistedBooks } })
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
            self.postMessage({
                type: "clear-persisted-complete",
                payload: {
                    cleared: false,
                    reason: "invalid",
                },
            })
            return
        }

        try {
            await Promise.all([deletePersistedIndex(hash), deletePersistedLibraryBooks(hash)])

            if (activeHash === hash) {
                resetInMemoryIndex()
            }

            self.postMessage({
                type: "clear-persisted-complete",
                payload: {
                    cleared: true,
                },
            })
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
            // Phase 1: Parse INPX in this worker — no main-thread round-trip
            const { metadata, books, datasetSignature } = parseInpxBuffer(
                buffer,
                (phase, processed, total, percent) => {
                    self.postMessage({
                        type: "build-progress",
                        payload: { phase, processed, total, percent },
                    })
                },
            )

            // Phase 2: Build FlexSearch index
            const total = books.length

            for (let start = 0; start < total; start += batchSize) {
                const end = Math.min(start + batchSize, total)
                const batchDocuments = []

                for (let i = start; i < end; i++) {
                    const book = books[i]
                    const id = String(book.id)
                    booksById.set(id, book)
                    batchDocuments.push({ id, content: toSearchText(book) })
                }

                await addBatchToIndexAsync(index, batchDocuments)

                self.postMessage({
                    type: "build-progress",
                    payload: {
                        phase: "indexing",
                        processed: end,
                        total,
                        percent: Math.round((end / total) * 100),
                    },
                })

                await new Promise((resolve) => setTimeout(resolve, BATCH_YIELD_DELAY_MS))
            }

            // Phase 3: Export index chunks + persist to IDB
            const chunks = await exportIndex(index)
            let persisted = false
            let persistenceError = ""

            if (hash) {
                try {
                    await Promise.all([
                        writePersistedIndex(hash, datasetSignature, chunks, total, metadata),
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

            // Transfer index + books to main thread for local search
            self.postMessage({ type: "index-data", payload: { chunks, books } })
        } catch (err) {
            resetInMemoryIndex()
            self.postMessage({
                type: "build-error",
                message: err instanceof Error ? err.message : "Failed to parse and build index.",
            })
        }

        return
    }
}

function toSearchText(book: BookRecord): string {
    return [book.title, book.authors, (book as any).series, book.lang, book.file]
        .filter(Boolean)
        .map(normalizeSearchValue)
        .join(" ")
}
