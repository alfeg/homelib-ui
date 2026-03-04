import type { BookRecord, LibraryMetadata } from "../types/library"
import SearchIndexWorker from "../workers/searchIndex.worker.ts?worker&inline"

export interface RestoreResult {
    restored: boolean
    reason?: string
    metadata?: LibraryMetadata
    total?: number
    persistedAt?: string
}

const _t0 = performance.now()
const ts = () => `+${(performance.now() - _t0).toFixed(0)}ms`

export interface SearchQuery {
    term: string
    page: number
    pageSize: number
    genres: string[]
    yearFrom?: number
    yearTo?: number
}

export interface SearchResult {
    books: BookRecord[]
    total: number
    genres: { genre: string; count: number }[]
    yearRange?: { min: number; max: number } | null
}

type BuildResult = {
    total?: number
    persisted?: boolean
    metadata?: LibraryMetadata | null
    datasetSignature?: string
    persistenceError?: string
}

type ClearPersistedResult = {
    cleared: boolean
    reason?: string
    message?: string
}

type Pending<T> = { resolve: (value: T) => void; reject: (reason?: unknown) => void } | null

type BuildProgressPayload = {
    phase?: string
    processed?: number
    total?: number
    percent?: number
    downloadedBytes?: number
    totalBytes?: number | null
}

export function createSearchWorkerClient({
    onProgress,
    onError,
}: {
    onProgress?: (payload: BuildProgressPayload) => void
    onError?: (error: Error) => void
} = {}) {
    const worker = new SearchIndexWorker()
    let pendingBuild: Pending<BuildResult> = null
    let pendingRestore: Pending<RestoreResult> = null
    let pendingClear: Pending<ClearPersistedResult> = null
    let pendingClearAll: Pending<ClearPersistedResult> = null
    let pendingSearch: {
        resolve: (value: SearchResult) => void
        reject: (reason?: unknown) => void
        requestId: number
    } | null = null
    let searchRequestCounter = 0

    worker.onmessage = (event) => {
        const message = event?.data ?? {}
        if (message.type !== "build-progress" && message.type !== "search-result") {
            console.debug(`[worker ${ts()}] recv type="${message.type}"`, message.payload ?? "")
        }

        if (message.type === "build-error") {
            const err = new Error(message.message || "Index build failed.")
            console.error(`[worker ${ts()}] build-error:`, err.message)
            pendingBuild?.reject(err)
            pendingBuild = null
            onError?.(err)
            return
        }

        if (message.type === "build-progress") {
            onProgress?.(message.payload ?? {})
            return
        }

        if (message.type === "build-complete") {
            onProgress?.({
                phase: "indexing",
                processed: message.payload?.total ?? 0,
                total: message.payload?.total ?? 0,
                percent: 100,
            })
            pendingBuild?.resolve(message.payload ?? { total: 0, persisted: false })
            pendingBuild = null
            return
        }

        if (message.type === "restore-complete") {
            pendingRestore?.resolve(message.payload ?? { restored: false, reason: "unknown" })
            pendingRestore = null
            return
        }

        if (message.type === "clear-persisted-complete") {
            pendingClear?.resolve(message.payload ?? { cleared: false, reason: "unknown" })
            pendingClear = null
            return
        }

        if (message.type === "clear-all-persisted-complete") {
            pendingClearAll?.resolve(message.payload ?? { cleared: false, reason: "unknown" })
            pendingClearAll = null
            return
        }

        if (message.type === "search-result") {
            if (pendingSearch && message.requestId === pendingSearch.requestId) {
                pendingSearch.resolve(message.payload as SearchResult)
                pendingSearch = null
            }
            return
        }
    }

    worker.onerror = (event) => {
        const err = new Error(event?.message || "Search worker failed.")
        console.error(`[worker ${ts()}] runtime error:`, err)
        pendingBuild?.reject(err)
        pendingBuild = null
        pendingRestore?.reject(err)
        pendingRestore = null
        pendingClear?.reject(err)
        pendingClear = null
        pendingClearAll?.reject(err)
        pendingClearAll = null
        pendingSearch?.reject(err)
        pendingSearch = null
        onError?.(err)
    }

    return {
        parseAndBuild(buffer: ArrayBuffer, { hash = "", batchSize }: { hash?: string; batchSize?: number } = {}) {
            if (pendingBuild) {
                pendingBuild.reject(new Error("Index build interrupted by a new build request."))
            }
            return new Promise<BuildResult>((resolve, reject) => {
                pendingBuild = { resolve, reject }
                worker.postMessage({ type: "parse-and-build", buffer, hash, batchSize }, [buffer])
            })
        },
        restoreIndex({
            hash = "",
            signature = "",
        }: { hash?: string; signature?: string } = {}): Promise<RestoreResult> {
            if (pendingRestore) {
                pendingRestore.reject(new Error("Index restore interrupted by a new restore request."))
            }
            return new Promise<RestoreResult>((resolve, reject) => {
                pendingRestore = { resolve, reject }
                worker.postMessage({ type: "restore", hash, signature })
            })
        },
        clearPersistedIndex(hash: string) {
            if (pendingClear) {
                pendingClear.reject(new Error("Persisted index clear interrupted by a new clear request."))
            }
            return new Promise<ClearPersistedResult>((resolve, reject) => {
                pendingClear = { resolve, reject }
                worker.postMessage({ type: "clear-persisted", hash })
            })
        },
        clearAllPersisted() {
            if (pendingClearAll) {
                pendingClearAll.reject(new Error("Global persisted data clear interrupted by a new clear request."))
            }
            return new Promise<ClearPersistedResult>((resolve, reject) => {
                pendingClearAll = { resolve, reject }
                worker.postMessage({ type: "clear-all-persisted" })
            })
        },
        search(query: SearchQuery): Promise<SearchResult> {
            const requestId = ++searchRequestCounter
            // Supersede any pending search — worker will still process it but result is ignored
            pendingSearch?.reject(Object.assign(new Error("superseded"), { superseded: true }))
            return new Promise((resolve, reject) => {
                pendingSearch = { resolve, reject, requestId }
                worker.postMessage({ type: "search", requestId, ...query })
            })
        },
    }
}
