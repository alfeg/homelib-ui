import type { BookRecord } from "../types/library"
import SearchIndexWorker from "../workers/searchIndex.worker.ts?worker"

const _t0 = performance.now()
const ts = () => `+${(performance.now() - _t0).toFixed(0)}ms`

export interface SearchResult {
    books: BookRecord[]
    total: number
    genres: { genre: string; count: number }[]
}

export function createSearchWorkerClient({
    onProgress,
    onError,
}: { onProgress?: (payload: any) => void; onError?: (error: Error) => void } = {}) {
    const worker = new SearchIndexWorker({ type: "module" })
    const pendingSearches = new Map<number, (result: SearchResult) => void>()
    let pendingBuild: { resolve: (value: any) => void; reject: (reason?: unknown) => void } | null = null
    let pendingRestore: { resolve: (value: any) => void; reject: (reason?: unknown) => void } | null = null
    let pendingClear: { resolve: (value: any) => void; reject: (reason?: unknown) => void } | null = null

    worker.onmessage = (event) => {
        const message = event?.data ?? {}
        if (message.type !== "build-progress") {
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

        if (message.type === "search-result") {
            const requestId = Number(message.payload?.requestId)
            console.debug(
                `[worker ${ts()}] search-result #${requestId} books=${message.payload?.books?.length ?? 0} total=${message.payload?.total ?? 0} pending=${pendingSearches.size}`,
            )
            if (!pendingSearches.has(requestId)) {
                console.warn(`[worker ${ts()}] search-result #${requestId} has no pending resolve (already cancelled?)`)
                return
            }

            const resolve = pendingSearches.get(requestId)!
            pendingSearches.delete(requestId)
            resolve({
                books: message.payload?.books ?? [],
                total: message.payload?.total ?? 0,
                genres: message.payload?.genres ?? [],
            })
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

        pendingSearches.forEach((resolve) => resolve({ books: [], total: 0, genres: [] }))
        pendingSearches.clear()

        onError?.(err)
    }

    return {
        parseAndBuild(buffer: ArrayBuffer, { hash = "", batchSize }: { hash?: string; batchSize?: number } = {}) {
            if (pendingBuild) {
                pendingBuild.reject(new Error("Index build interrupted by a new build request."))
            }

            return new Promise((resolve, reject) => {
                pendingBuild = { resolve, reject }
                worker.postMessage({ type: "parse-and-build", buffer, hash, batchSize }, [buffer])
            })
        },
        restoreIndex({ hash = "", signature = "" }: { hash?: string; signature?: string } = {}) {
            if (pendingRestore) {
                pendingRestore.reject(new Error("Index restore interrupted by a new restore request."))
            }

            return new Promise((resolve, reject) => {
                pendingRestore = { resolve, reject }
                worker.postMessage({ type: "restore", hash, signature })
            })
        },
        clearPersistedIndex(hash: string) {
            if (pendingClear) {
                pendingClear.reject(new Error("Persisted index clear interrupted by a new clear request."))
            }

            return new Promise((resolve, reject) => {
                pendingClear = { resolve, reject }
                worker.postMessage({ type: "clear-persisted", hash })
            })
        },
        search(
            term: string,
            requestId: number,
            page = 1,
            pageSize = 200,
            genres: string[] = [],
        ): Promise<SearchResult> {
            console.debug(
                `[worker ${ts()}] search send #${requestId} term="${term}" page=${page} pageSize=${pageSize} genres=[${genres}] pending=${pendingSearches.size}`,
            )
            return new Promise<SearchResult>((resolve) => {
                pendingSearches.set(requestId, resolve)
                worker.postMessage({
                    type: "search",
                    term,
                    requestId,
                    page,
                    pageSize,
                    genres: genres.length ? [...genres] : undefined,
                })
            })
        },
    }
}
