import { computed, reactive, ref, watch } from "https://unpkg.com/vue@3/dist/vue.esm-browser.prod.js";
import { apiClient } from "../services/apiClient.js";
import { parseHashFromMagnet, magnetStore } from "../services/magnetService.js";
import { libraryCacheStore } from "../services/storageService.js";
import { createSearchWorkerClient } from "../services/searchIndexWorkerClient.js";

const RESULTS_PAGE_SIZE = 200;
const SEARCH_RESULTS_LIMIT = 1000;
const INPX_PARSER_WORKER_URL = new URL("../workers/inpxParser.worker.js", import.meta.url);

function formatMegabytes(bytes) {
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function parseInpxWithWorker(buffer, onProgress) {
    if (typeof Worker === "undefined") {
        return Promise.reject(new Error("Web Worker is not supported in this browser."));
    }

    return new Promise((resolve, reject) => {
        const worker = new Worker(INPX_PARSER_WORKER_URL, { type: "module" });

        const cleanup = () => {
            worker.onmessage = null;
            worker.onerror = null;
            worker.terminate();
        };

        worker.onmessage = (event) => {
            const message = event?.data ?? {};

            if (message.type === "progress") {
                onProgress?.(message.payload ?? {});
                return;
            }

            if (message.type === "result") {
                cleanup();
                resolve(message.payload ?? { metadata: null, books: [] });
                return;
            }

            if (message.type === "error") {
                cleanup();
                reject(new Error(message.message || "Failed to parse INPX payload."));
            }
        };

        worker.onerror = (event) => {
            cleanup();
            reject(new Error(event?.message || "Failed to parse INPX payload."));
        };

        worker.postMessage({ type: "parse", buffer }, [buffer]);
    });
}

function saveBlob(blob, fileName) {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName;
    anchor.style.display = "none";
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
}

export function useLibraryState() {
    const magnetUri = ref("");
    const magnetHash = ref("");
    const metadata = ref(null);
    const books = ref([]);
    const filteredBooks = ref([]);
    const pagedBooks = ref([]);
    const searchTerm = ref("");
    const isLoading = ref(false);
    const isReindexing = ref(false);
    const status = ref("");
    const error = ref("");
    const hasCache = ref(false);
    const lastUpdatedAt = ref("");
    const currentPage = ref(1);

    const downloadingById = reactive({});
    const indexProgress = reactive({
        phase: "idle",
        processed: 0,
        total: 0,
        percent: 0,
        downloadedBytes: 0,
        totalBytes: null
    });

    const isMagnetSet = computed(() => !!magnetUri.value);
    const totalPages = computed(() => {
        const totalItems = filteredBooks.value.length;
        return totalItems ? Math.max(1, Math.ceil(totalItems / RESULTS_PAGE_SIZE)) : 1;
    });

    const visibleRange = computed(() => {
        if (!filteredBooks.value.length) {
            return { start: 0, end: 0 };
        }

        const start = (currentPage.value - 1) * RESULTS_PAGE_SIZE + 1;
        const end = Math.min(currentPage.value * RESULTS_PAGE_SIZE, filteredBooks.value.length);
        return { start, end };
    });

    const searchWorkerClient = createSearchWorkerClient({
        onProgress: (progress) => {
            setProgress(progress);
            status.value = `Building search index... ${progress.processed}/${progress.total} (${progress.percent}%)`;
        },
        onError: (err) => {
            error.value = err instanceof Error ? err.message : "Search index worker failed.";
        }
    });

    let searchRequestId = 0;
    let activeSearchRequestId = 0;

    function setProgress(next) {
        indexProgress.phase = next.phase ?? indexProgress.phase;
        indexProgress.processed = next.processed ?? indexProgress.processed;
        indexProgress.total = next.total ?? indexProgress.total;
        indexProgress.percent = next.percent ?? indexProgress.percent;

        if ("downloadedBytes" in next) {
            indexProgress.downloadedBytes = next.downloadedBytes;
        }

        if ("totalBytes" in next) {
            indexProgress.totalBytes = next.totalBytes;
        }
    }

    function resetProgress() {
        setProgress({
            phase: "idle",
            processed: 0,
            total: 0,
            percent: 0,
            downloadedBytes: 0,
            totalBytes: null
        });
    }

    function updatePagedBooks() {
        const startIndex = (currentPage.value - 1) * RESULTS_PAGE_SIZE;
        pagedBooks.value = filteredBooks.value.slice(startIndex, startIndex + RESULTS_PAGE_SIZE);
    }

    watch([filteredBooks, currentPage], () => {
        if (currentPage.value > totalPages.value) {
            currentPage.value = totalPages.value;
            return;
        }

        updatePagedBooks();
    }, { immediate: true });

    watch(searchTerm, async () => {
        currentPage.value = 1;
        await refreshSearchResults();
    });

    async function refreshSearchResults() {
        const term = searchTerm.value.trim();

        if (!term) {
            activeSearchRequestId += 1;
            filteredBooks.value = books.value;
            return;
        }

        if (indexProgress.phase !== "ready") {
            filteredBooks.value = [];
            return;
        }

        const requestId = ++searchRequestId;
        activeSearchRequestId = requestId;
        const matches = await searchWorkerClient.search(term, requestId, SEARCH_RESULTS_LIMIT);

        if (requestId !== activeSearchRequestId) return;
        filteredBooks.value = matches;
    }

    async function applyBooks(payload, fromCache) {
        metadata.value = payload.metadata ?? null;
        books.value = payload.books ?? [];
        filteredBooks.value = searchTerm.value.trim() ? [] : books.value;
        currentPage.value = 1;

        setProgress({
            phase: "indexing",
            processed: 0,
            total: books.value.length,
            percent: 0,
            downloadedBytes: 0,
            totalBytes: null
        });
        status.value = `Building search index... 0/${books.value.length} (0%)`;

        await searchWorkerClient.buildIndex(books.value);

        hasCache.value = fromCache;
        setProgress({
            phase: "ready",
            processed: books.value.length,
            total: books.value.length,
            percent: 100,
            downloadedBytes: 0,
            totalBytes: null
        });
        status.value = fromCache ? "Loaded from local cache." : "Library indexed from backend.";

        await refreshSearchResults();
    }

    async function cachePayload(hash, payload) {
        await libraryCacheStore.save({
            hash,
            magnetUri: magnetUri.value,
            metadata: payload.metadata,
            books: payload.books,
            indexMeta: {
                count: payload.books?.length ?? 0,
                cachedAt: new Date().toISOString()
            }
        });
    }

    async function fetchBooksViaInpx(forceReindex) {
        setProgress({
            phase: "loading-backend",
            processed: 0,
            total: 0,
            percent: 0,
            downloadedBytes: 0,
            totalBytes: null
        });
        status.value = forceReindex
            ? "Reindexing library: downloading INPX from backend..."
            : "Loading library: downloading INPX from backend...";

        const inpxBuffer = await apiClient.fetchInpx(magnetUri.value, forceReindex, ({ downloadedBytes, totalBytes, percent }) => {
            setProgress({
                phase: "loading-backend",
                downloadedBytes,
                totalBytes,
                percent: percent ?? 0
            });

            if (totalBytes) {
                status.value = `Downloading INPX: ${formatMegabytes(downloadedBytes)} / ${formatMegabytes(totalBytes)} (${percent ?? 0}%)`;
                return;
            }

            status.value = `Downloading INPX: ${formatMegabytes(downloadedBytes)} downloaded`;
        });

        setProgress({
            phase: "parsing",
            processed: 0,
            total: 0,
            percent: 0,
            downloadedBytes: 0,
            totalBytes: null
        });
        status.value = "INPX downloaded. Parsing on client...";

        return parseInpxWithWorker(inpxBuffer, (progress) => {
            setProgress({
                phase: "parsing",
                processed: progress.processed ?? 0,
                total: progress.total ?? 0,
                percent: progress.percent ?? 0,
                downloadedBytes: 0,
                totalBytes: null
            });

            const total = progress.total ?? 0;
            const processed = progress.processed ?? 0;
            const percent = progress.percent ?? 0;
            status.value = total
                ? `Parsing INPX... ${processed}/${total} (${percent}%)`
                : `Parsing INPX... (${percent}%)`;
        });
    }

    async function fetchAndApply(forceReindex = false) {
        if (!magnetUri.value || !magnetHash.value) return;

        error.value = "";
        isLoading.value = !forceReindex;
        isReindexing.value = forceReindex;

        try {
            let payload;

            try {
                payload = await fetchBooksViaInpx(forceReindex);
            } catch {
                setProgress({
                    phase: "loading-backend",
                    processed: 0,
                    total: 0,
                    percent: 0,
                    downloadedBytes: 0,
                    totalBytes: null
                });
                status.value = "Client-side INPX parse failed. Falling back to backend payload...";

                payload = await apiClient.fetchBooks(magnetUri.value, forceReindex, ({ downloadedBytes, totalBytes, percent }) => {
                    setProgress({
                        phase: "loading-backend",
                        downloadedBytes,
                        totalBytes,
                        percent: percent ?? 0
                    });

                    if (totalBytes) {
                        status.value = `Downloading library payload: ${formatMegabytes(downloadedBytes)} / ${formatMegabytes(totalBytes)} (${percent ?? 0}%)`;
                        return;
                    }

                    status.value = `Downloading library payload: ${formatMegabytes(downloadedBytes)} downloaded`;
                });
            }

            status.value = "Library data loaded. Building search index...";
            await applyBooks(payload, false);
            await cachePayload(magnetHash.value, payload);
            lastUpdatedAt.value = new Date().toLocaleString();
        } finally {
            isLoading.value = false;
            isReindexing.value = false;
        }
    }

    async function loadLibraryForCurrentMagnet() {
        if (!magnetHash.value) return;

        setProgress({
            phase: "loading-cache",
            processed: 0,
            total: 0,
            percent: 0,
            downloadedBytes: 0,
            totalBytes: null
        });
        status.value = "Loading library from local cache...";
        const cached = await libraryCacheStore.getByHash(magnetHash.value);
        if (cached?.books?.length) {
            status.value = "Cached library loaded. Building search index...";
            await applyBooks(cached, true);
            lastUpdatedAt.value = cached.updatedAt ? new Date(cached.updatedAt).toLocaleString() : "";
            return;
        }

        await fetchAndApply(false);
    }

    async function submitMagnet(uri) {
        error.value = "";
        const clean = uri.trim();
        if (!clean) {
            error.value = "Magnet URI is required.";
            return;
        }

        try {
            const hash = parseHashFromMagnet(clean);
            magnetUri.value = clean;
            magnetHash.value = hash;
            magnetStore.set(clean);
            await loadLibraryForCurrentMagnet();
        } catch (err) {
            error.value = err instanceof Error ? err.message : "Invalid magnet URI.";
        }
    }

    async function bootstrap() {
        try {
            await apiClient.getUserId();
        } catch {
            // session fetch is optional for UI boot
        }

        const stored = magnetStore.get();
        if (!stored) return;

        magnetUri.value = stored;

        try {
            magnetHash.value = parseHashFromMagnet(stored);
            await loadLibraryForCurrentMagnet();
        } catch {
            magnetStore.clear();
            magnetUri.value = "";
            magnetHash.value = "";
            resetProgress();
        }
    }

    async function reindexCurrent() {
        try {
            await fetchAndApply(true);
        } catch (err) {
            error.value = err instanceof Error ? err.message : "Failed to reindex.";
        }
    }

    async function resetAll() {
        magnetStore.clear();
        await libraryCacheStore.clearAll();

        magnetUri.value = "";
        magnetHash.value = "";
        metadata.value = null;
        books.value = [];
        filteredBooks.value = [];
        pagedBooks.value = [];
        searchTerm.value = "";
        currentPage.value = 1;
        status.value = "";
        error.value = "";
        hasCache.value = false;
        lastUpdatedAt.value = "";
        resetProgress();
    }

    function goToPage(page) {
        const nextPageValue = Math.min(Math.max(page, 1), totalPages.value);
        currentPage.value = nextPageValue;
    }

    function nextPage() {
        goToPage(currentPage.value + 1);
    }

    function previousPage() {
        goToPage(currentPage.value - 1);
    }

    async function downloadBook(book) {
        if (!book || !magnetUri.value) return;

        downloadingById[book.id] = true;
        error.value = "";

        try {
            const { blob, fileName } = await apiClient.downloadBook({
                magnetUri: magnetUri.value,
                archiveFile: book.archiveFile,
                file: book.file,
                ext: book.ext,
                title: book.title,
                authors: book.authors
            });
            saveBlob(blob, fileName || `${book.file}.${book.ext}`);
        } catch (err) {
            error.value = err instanceof Error ? err.message : "Download failed.";
        } finally {
            downloadingById[book.id] = false;
        }
    }

    return {
        magnetUri,
        magnetHash,
        metadata,
        books,
        filteredBooks,
        pagedBooks,
        searchTerm,
        isLoading,
        isReindexing,
        isMagnetSet,
        status,
        error,
        hasCache,
        lastUpdatedAt,
        indexProgress,
        downloadingById,
        currentPage,
        totalPages,
        visibleRange,
        pageSize: RESULTS_PAGE_SIZE,
        submitMagnet,
        bootstrap,
        reindexCurrent,
        resetAll,
        downloadBook,
        goToPage,
        nextPage,
        previousPage
    };
}
