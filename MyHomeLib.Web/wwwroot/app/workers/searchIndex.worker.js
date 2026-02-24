import { Index as FlexIndex } from "https://cdn.jsdelivr.net/npm/flexsearch@0.8.212/dist/flexsearch.bundle.module.min.js";

const DEFAULT_INDEX_BATCH_SIZE = 1024;
const MIN_INDEX_BATCH_SIZE = 500;
const MAX_INDEX_BATCH_SIZE = 2000;
const BATCH_YIELD_DELAY_MS = 0;
const PERSISTENCE_DB_NAME = "myhomelib-search-index-cache";
const PERSISTENCE_STORE_NAME = "indexes";
const PERSISTENCE_DB_VERSION = 1;

let index = null;
let booksById = new Map();
let activeHash = "";
let activeSignature = "";
let persistenceDbPromise = null;

function toSearchText(book) {
    return [book.title, book.authors, book.series, book.lang, book.file]
        .filter(Boolean)
        .join(" ");
}

function openPersistenceDb() {
    if (persistenceDbPromise) {
        return persistenceDbPromise;
    }

    persistenceDbPromise = new Promise((resolve, reject) => {
        const request = indexedDB.open(PERSISTENCE_DB_NAME, PERSISTENCE_DB_VERSION);

        request.onupgradeneeded = () => {
            const db = request.result;
            if (!db.objectStoreNames.contains(PERSISTENCE_STORE_NAME)) {
                db.createObjectStore(PERSISTENCE_STORE_NAME, { keyPath: "hash" });
            }
        };

        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error ?? new Error("Failed to open search index persistence DB."));
    });

    return persistenceDbPromise;
}

async function readPersistedIndex(hash) {
    const db = await openPersistenceDb();

    return new Promise((resolve, reject) => {
        const tx = db.transaction(PERSISTENCE_STORE_NAME, "readonly");
        const request = tx.objectStore(PERSISTENCE_STORE_NAME).get(hash);

        request.onsuccess = () => resolve(request.result ?? null);
        request.onerror = () => reject(request.error ?? new Error("Failed to read persisted search index."));
    });
}

async function writePersistedIndex(hash, signature, chunks, total) {
    const db = await openPersistenceDb();

    return new Promise((resolve, reject) => {
        const tx = db.transaction(PERSISTENCE_STORE_NAME, "readwrite");
        tx.objectStore(PERSISTENCE_STORE_NAME).put({
            hash,
            signature,
            chunks,
            total,
            updatedAt: new Date().toISOString()
        });

        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error ?? new Error("Failed to persist search index."));
    });
}

async function deletePersistedIndex(hash) {
    const db = await openPersistenceDb();

    return new Promise((resolve, reject) => {
        const tx = db.transaction(PERSISTENCE_STORE_NAME, "readwrite");
        tx.objectStore(PERSISTENCE_STORE_NAME).delete(hash);

        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error ?? new Error("Failed to clear persisted search index."));
    });
}

async function exportIndex(targetIndex) {
    const chunks = [];

    await targetIndex.export((key, data) => {
        if (key == null) return;
        chunks.push({ key, data });
    });

    return chunks;
}

async function importIndex(targetIndex, chunks) {
    const source = Array.isArray(chunks) ? chunks : [];

    for (let i = 0; i < source.length; i += 1) {
        const chunk = source[i];
        if (!chunk || chunk.key == null) continue;
        await targetIndex.import(chunk.key, chunk.data);
    }
}

function resetInMemoryIndex() {
    index = null;
    booksById = new Map();
    activeHash = "";
    activeSignature = "";
}

function resolveBatchSize(value) {
    const parsed = Number.parseInt(value, 10);

    if (!Number.isFinite(parsed)) {
        return DEFAULT_INDEX_BATCH_SIZE;
    }

    return Math.min(MAX_INDEX_BATCH_SIZE, Math.max(MIN_INDEX_BATCH_SIZE, parsed));
}

self.onmessage = async (event) => {
    const message = event?.data ?? {};

    if (message.type === "build") {
        const books = Array.isArray(message.books) ? message.books : [];
        const hash = typeof message.hash === "string" ? message.hash : "";
        const signature = typeof message.signature === "string" ? message.signature : "";
        const batchSize = resolveBatchSize(message.batchSize);
        const total = books.length;

        index = new FlexIndex({ tokenize: "forward", cache: true });
        booksById = new Map();
        activeHash = hash;
        activeSignature = signature;

        if (!total) {
            self.postMessage({
                type: "build-progress",
                payload: {
                    phase: "indexing",
                    processed: 0,
                    total: 0,
                    percent: 100
                }
            });

            if (hash && signature) {
                await writePersistedIndex(hash, signature, [], 0);
            }

            self.postMessage({
                type: "build-complete",
                payload: {
                    total: 0,
                    persisted: Boolean(hash && signature)
                }
            });
            return;
        }

        for (let start = 0; start < total; start += batchSize) {
            const end = Math.min(start + batchSize, total);

            for (let i = start; i < end; i += 1) {
                const book = books[i];
                const id = String(book.id);
                booksById.set(id, book);
                index.add(id, toSearchText(book));
            }

            self.postMessage({
                type: "build-progress",
                payload: {
                    phase: "indexing",
                    processed: end,
                    total,
                    percent: Math.round((end / total) * 100)
                }
            });

            await new Promise((resolve) => setTimeout(resolve, BATCH_YIELD_DELAY_MS));
        }

        let persisted = false;
        let persistenceError = "";

        if (hash && signature) {
            try {
                const chunks = await exportIndex(index);
                await writePersistedIndex(hash, signature, chunks, total);
                persisted = true;
            } catch (err) {
                persistenceError = err instanceof Error ? err.message : "Failed to persist index.";
            }
        }

        self.postMessage({
            type: "build-complete",
            payload: {
                total,
                persisted,
                persistenceError
            }
        });

        return;
    }

    if (message.type === "restore") {
        const books = Array.isArray(message.books) ? message.books : [];
        const hash = typeof message.hash === "string" ? message.hash : "";
        const signature = typeof message.signature === "string" ? message.signature : "";

        if (!hash || !signature) {
            self.postMessage({
                type: "restore-complete",
                payload: {
                    restored: false,
                    reason: "invalid"
                }
            });
            return;
        }

        try {
            const persisted = await readPersistedIndex(hash);
            if (!persisted) {
                self.postMessage({
                    type: "restore-complete",
                    payload: {
                        restored: false,
                        reason: "missing"
                    }
                });
                return;
            }

            if (persisted.signature !== signature) {
                self.postMessage({
                    type: "restore-complete",
                    payload: {
                        restored: false,
                        reason: "stale"
                    }
                });
                return;
            }

            const restoredIndex = new FlexIndex({ tokenize: "forward", cache: true });
            await importIndex(restoredIndex, persisted.chunks);

            index = restoredIndex;
            booksById = new Map(books.map((book) => [String(book.id), book]));
            activeHash = hash;
            activeSignature = signature;

            self.postMessage({
                type: "restore-complete",
                payload: {
                    restored: true,
                    total: persisted.total ?? books.length,
                    persistedAt: persisted.updatedAt ?? ""
                }
            });
        } catch (err) {
            self.postMessage({
                type: "restore-complete",
                payload: {
                    restored: false,
                    reason: "error",
                    message: err instanceof Error ? err.message : "Failed to restore search index."
                }
            });
        }

        return;
    }

    if (message.type === "clear-persisted") {
        const hash = typeof message.hash === "string" ? message.hash : "";

        if (!hash) {
            self.postMessage({
                type: "clear-persisted-complete",
                payload: {
                    cleared: false,
                    reason: "invalid"
                }
            });
            return;
        }

        try {
            await deletePersistedIndex(hash);

            if (activeHash === hash) {
                resetInMemoryIndex();
            }

            self.postMessage({
                type: "clear-persisted-complete",
                payload: {
                    cleared: true
                }
            });
        } catch (err) {
            self.postMessage({
                type: "clear-persisted-complete",
                payload: {
                    cleared: false,
                    reason: "error",
                    message: err instanceof Error ? err.message : "Failed to clear persisted index."
                }
            });
        }

        return;
    }

    if (message.type === "search") {
        const requestId = message.requestId;
        const term = typeof message.term === "string" ? message.term.trim() : "";
        const limit = Number.isFinite(message.limit) ? message.limit : 1000;

        if (!term || !index) {
            self.postMessage({
                type: "search-result",
                payload: {
                    requestId,
                    books: []
                }
            });
            return;
        }

        const books = index.search(term, { limit })
            .map((id) => booksById.get(String(id)))
            .filter(Boolean);

        self.postMessage({
            type: "search-result",
            payload: {
                requestId,
                books
            }
        });
    }
};
