import { computed, reactive, ref } from "https://unpkg.com/vue@3/dist/vue.esm-browser.prod.js";
import { Index as FlexIndex } from "https://cdn.jsdelivr.net/npm/flexsearch@0.8.212/dist/flexsearch.bundle.module.min.js";
import { apiClient } from "../services/apiClient.js";
import { parseHashFromMagnet, magnetStore } from "../services/magnetService.js";
import { libraryCacheStore } from "../services/storageService.js";

const INDEX_CHUNK_SIZE = 250;

function yieldToBrowser() {
    if (typeof window !== "undefined" && typeof window.requestAnimationFrame === "function") {
        return new Promise((resolve) => window.requestAnimationFrame(() => resolve()));
    }

    return new Promise((resolve) => setTimeout(resolve, 0));
}

async function createSearchIndexAsync(books, onProgress) {
    const index = new FlexIndex({ tokenize: "forward", cache: true });
    const byId = new Map();
    const total = books.length;

    if (!total) {
        onProgress?.({ phase: "indexing", processed: 0, total: 0, percent: 100 });
        return { index, byId };
    }

    for (let i = 0; i < total; i += 1) {
        const book = books[i];
        const id = String(book.id);
        byId.set(id, book);

        const text = [book.title, book.authors, book.series, book.lang, book.file]
            .filter(Boolean)
            .join(" ");
        index.add(id, text);

        const processed = i + 1;
        const shouldYield = processed % INDEX_CHUNK_SIZE === 0 || processed === total;

        if (shouldYield) {
            onProgress?.({
                phase: "indexing",
                processed,
                total,
                percent: Math.round((processed / total) * 100)
            });
            await yieldToBrowser();
        }
    }

    return { index, byId };
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
    const searchTerm = ref("");
    const isLoading = ref(false);
    const isReindexing = ref(false);
    const status = ref("");
    const error = ref("");
    const hasCache = ref(false);
    const lastUpdatedAt = ref("");

    const indexState = ref({ index: null, byId: new Map() });
    const downloadingById = reactive({});
    const indexProgress = reactive({
        phase: "idle",
        processed: 0,
        total: 0,
        percent: 0
    });

    const isMagnetSet = computed(() => !!magnetUri.value);
    const filteredBooks = computed(() => {
        const term = searchTerm.value.trim();
        if (!term) return books.value;

        const idx = indexState.value.index;
        if (!idx) return [];

        const results = idx.search(term, { limit: 1000 });
        return results
            .map((id) => indexState.value.byId.get(String(id)))
            .filter(Boolean);
    });

    function setProgress(next) {
        indexProgress.phase = next.phase ?? indexProgress.phase;
        indexProgress.processed = next.processed ?? indexProgress.processed;
        indexProgress.total = next.total ?? indexProgress.total;
        indexProgress.percent = next.percent ?? indexProgress.percent;
    }

    function resetProgress() {
        setProgress({
            phase: "idle",
            processed: 0,
            total: 0,
            percent: 0
        });
    }

    async function applyBooks(payload, fromCache) {
        metadata.value = payload.metadata ?? null;
        books.value = payload.books ?? [];

        setProgress({ phase: "indexing", processed: 0, total: books.value.length, percent: 0 });
        status.value = `Building search index... 0/${books.value.length} (0%)`;
        indexState.value = { index: null, byId: new Map() };

        indexState.value = await createSearchIndexAsync(books.value, (progress) => {
            setProgress(progress);
            status.value = `Building search index... ${progress.processed}/${progress.total} (${progress.percent}%)`;
        });

        hasCache.value = fromCache;
        setProgress({ phase: "ready", processed: books.value.length, total: books.value.length, percent: 100 });
        status.value = fromCache ? "Loaded from local cache." : "Library indexed from backend.";
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

    async function fetchAndApply(forceReindex = false) {
        if (!magnetUri.value || !magnetHash.value) return;

        error.value = "";
        setProgress({ phase: "loading-backend", processed: 0, total: 0, percent: 0 });
        status.value = forceReindex ? "Reindexing library: fetching data from backend..." : "Loading library from backend...";
        isLoading.value = !forceReindex;
        isReindexing.value = forceReindex;

        try {
            const payload = await apiClient.fetchBooks(magnetUri.value, forceReindex);
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

        setProgress({ phase: "loading-cache", processed: 0, total: 0, percent: 0 });
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
        searchTerm.value = "";
        status.value = "";
        error.value = "";
        hasCache.value = false;
        lastUpdatedAt.value = "";
        indexState.value = { index: null, byId: new Map() };
        resetProgress();
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
        submitMagnet,
        bootstrap,
        reindexCurrent,
        resetAll,
        downloadBook
    };
}
