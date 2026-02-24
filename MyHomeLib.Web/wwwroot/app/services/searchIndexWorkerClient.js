const SEARCH_INDEX_WORKER_URL = new URL("../workers/searchIndex.worker.js", import.meta.url);

export function createSearchWorkerClient({ onProgress, onError } = {}) {
    if (typeof Worker === "undefined") {
        throw new Error("Web Worker is not supported in this browser.");
    }

    const worker = new Worker(SEARCH_INDEX_WORKER_URL, { type: "module" });
    const pendingSearches = new Map();
    let pendingBuild = null;

    worker.onmessage = (event) => {
        const message = event?.data ?? {};

        if (message.type === "build-progress") {
            onProgress?.(message.payload ?? {});
            return;
        }

        if (message.type === "build-complete") {
            onProgress?.({
                phase: "indexing",
                processed: message.payload?.total ?? 0,
                total: message.payload?.total ?? 0,
                percent: 100
            });

            pendingBuild?.resolve(message.payload ?? { total: 0 });
            pendingBuild = null;
            return;
        }

        if (message.type === "search-result") {
            const requestId = message.payload?.requestId;
            if (!pendingSearches.has(requestId)) return;

            const resolve = pendingSearches.get(requestId);
            pendingSearches.delete(requestId);
            resolve(message.payload?.books ?? []);
        }
    };

    worker.onerror = (event) => {
        const err = new Error(event?.message || "Search worker failed.");

        pendingBuild?.reject(err);
        pendingBuild = null;

        pendingSearches.forEach((resolve) => resolve([]));
        pendingSearches.clear();

        onError?.(err);
    };

    return {
        buildIndex(books) {
            if (pendingBuild) {
                pendingBuild.reject(new Error("Index build interrupted by a new build request."));
            }

            return new Promise((resolve, reject) => {
                pendingBuild = { resolve, reject };
                worker.postMessage({ type: "build", books });
            });
        },
        search(term, requestId, limit = 1000) {
            return new Promise((resolve) => {
                pendingSearches.set(requestId, resolve);
                worker.postMessage({ type: "search", term, requestId, limit });
            });
        },
        terminate() {
            worker.terminate();
            pendingBuild?.reject(new Error("Search worker terminated."));
            pendingBuild = null;
            pendingSearches.forEach((resolve) => resolve([]));
            pendingSearches.clear();
        }
    };
}