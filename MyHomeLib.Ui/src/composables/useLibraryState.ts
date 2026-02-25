/* eslint-disable */
import { createGlobalState } from "@vueuse/core";
import { computed, reactive, ref, watch } from "vue";
import { apiClient } from "../services/apiClient";
import { parseHashFromMagnet, magnetStore } from "../services/magnetService";
import { convertTorrentFileToMagnet } from "../services/torrentMagnetService";
import { createSearchWorkerClient } from "../services/searchIndexWorkerClient";
import { loadGenreLabels } from "../services/genreLabelsService";
import { getCurrentLocale, localeRef, translate } from "../services/i18n";

const RESULTS_PAGE_SIZE = 200;
const SEARCH_RESULTS_LIMIT = 1000;
const NO_GENRE_CODE = "__no_genre__";
const GENRE_DELIMITER = ":";

function formatMegabytes(bytes) {
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
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

function normalizeGenreCode(code) {
    return String(code ?? "").trim();
}

function normalizeSearchText(value) {
    return String(value ?? "")
        .toLocaleLowerCase("ru-RU")
        .replaceAll("ё", "е");
}

function uniqueStrings(values) {
    return Array.from(new Set(values.filter(Boolean)));
}

function parseRawGenreCodes(rawGenre) {
    return String(rawGenre ?? "")
        .split(GENRE_DELIMITER)
        .map((code) => normalizeGenreCode(code))
        .filter(Boolean);
}

export const useLibraryState = createGlobalState(() => {
    const t = translate;
    const magnetUri = ref("");
    const magnetHash = ref("");
    const metadata = ref(null);
    const searchMatchedBooks = ref([]);
    const filteredBooks = ref([]);
    const pagedBooks = ref([]);
    const selectedGenres = ref([]);
    const genreFacets = ref([]);
    const genreLabelByCode = ref(new Map());
    const searchTerm = ref("");
    const isLoading = ref(false);
    const isReindexing = ref(false);
    const status = ref("");
    const error = ref("");
    const hasCache = ref(false);
    const lastUpdatedAt = ref("");
    const currentPage = ref(1);
    const totalBooks = ref(0);

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
    const hasGenreFilters = computed(() => selectedGenres.value.length > 0);

    // genreFacets is populated once from ALL library books (not current search results)
    // and refreshed when locale changes or library reloads.

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
            status.value = t("status.indexing", { processed: progress.processed, total: progress.total, percent: progress.percent });
        },
        onError: (err) => {
            error.value = err instanceof Error ? err.message : t("error.searchWorkerFailed");
        }
    });

    // Single monotonic counter — every search (including empty-term) increments it.
    // After await, stale responses are discarded by comparing against the counter.
    let searchRequestId = 0;

    function resolveGenreLabel(genreCode) {
        if (genreCode === NO_GENRE_CODE) {
            return t("genres.noGenre");
        }

        return genreLabelByCode.value.get(genreCode) ?? genreCode;
    }

    function formatBookGenres(book) {
        const codes = Array.isArray(book?.genreCodes) && book.genreCodes.length
            ? book.genreCodes
            : parseRawGenreCodes(book?.genre);

        if (!codes.length) {
            return t("genres.noGenre");
        }

        return uniqueStrings(codes.map((code) => resolveGenreLabel(code))).join(", ");
    }

    function recomputeGenreFacetsFromWorkerData(workerGenres: { genre: string; count: number }[]) {
        genreFacets.value = workerGenres
            .map(({ genre, count }) => ({
                genre,
                label: resolveGenreLabel(genre),
                count
            }))
            .sort((a, b) => {
                if (b.count !== a.count) return b.count - a.count;
                return a.label.localeCompare(b.label, getCurrentLocale());
            });
    }

    async function loadAllGenres() {
        try {
            const workerGenres = await searchWorkerClient.getGenres();
            recomputeGenreFacetsFromWorkerData(workerGenres);
        } catch {
            // non-fatal — genre sidebar stays empty
        }
    }

    async function ensureGenreLabelsLoaded() {
        const labels = await loadGenreLabels();
        genreLabelByCode.value = labels;
        // Re-apply labels to already-loaded genre facets (if any)
        if (genreFacets.value.length) {
            genreFacets.value = genreFacets.value.map((f) => ({
                ...f,
                label: resolveGenreLabel(f.genre)
            }));
        }
    }

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

    watch(selectedGenres, async () => {
        currentPage.value = 1;
        await refreshSearchResults();
    }, { deep: true });

    watch(() => indexProgress.phase, async (phase) => {
        if (phase === "ready") {
            await refreshSearchResults();
        }
    });

    watch(localeRef, async () => {
        await ensureGenreLabelsLoaded();
        if (indexProgress.phase === "ready") {
            await loadAllGenres();
        }
    });

    async function refreshSearchResults() {
        if (indexProgress.phase !== "ready") return;

        const term = searchTerm.value.trim();
        const genres = selectedGenres.value;
        const requestId = ++searchRequestId;

        const matches = await searchWorkerClient.search(term, requestId, SEARCH_RESULTS_LIMIT, genres);

        // Discard if a newer search has started
        if (requestId !== searchRequestId) return;

        searchMatchedBooks.value = matches;
        filteredBooks.value = matches;
    }

    function clearGenreFilters() {
        selectedGenres.value = [];
    }

    function toggleGenreFilter(genre) {
        const normalizedGenre = normalizeGenreCode(genre);
        if (!normalizedGenre) return;

        if (selectedGenres.value.includes(normalizedGenre)) {
            selectedGenres.value = selectedGenres.value.filter((item) => item !== normalizedGenre);
            return;
        }

        selectedGenres.value = [...selectedGenres.value, normalizedGenre];
    }

    async function fetchAndBuild({ reindexing = false } = {}) {
        if (!magnetUri.value || !magnetHash.value) return;

        error.value = "";
        isLoading.value = !reindexing;
        isReindexing.value = reindexing;

        try {
            // Step 1: Download INPX
            setProgress({ phase: "loading-backend", processed: 0, total: 0, percent: 0, downloadedBytes: 0, totalBytes: null });
            status.value = reindexing ? t("status.reindexDownloading") : t("status.loadingDownloading");

            const inpxBuffer = await apiClient.fetchInpx(magnetUri.value, ({ downloadedBytes, totalBytes, percent }) => {
                setProgress({ phase: "loading-backend", downloadedBytes, totalBytes, percent: percent ?? 0 });
                status.value = totalBytes
                    ? t("status.downloadingInpxTotal", { downloaded: formatMegabytes(downloadedBytes), total: formatMegabytes(totalBytes), percent: percent ?? 0 })
                    : t("status.downloadingInpxSimple", { downloaded: formatMegabytes(downloadedBytes) });
            });

            // Step 2: Transfer buffer to worker — parse + index + persist, no books on main thread
            status.value = t("status.inpxDownloadedParsing");
            const buildResult: any = await searchWorkerClient.parseAndBuild(inpxBuffer, { hash: magnetHash.value });

            metadata.value = buildResult?.metadata ?? null;
            totalBooks.value = buildResult?.total ?? 0;
            lastUpdatedAt.value = new Date().toLocaleString();
            hasCache.value = false;

            setProgress({ phase: "ready", processed: totalBooks.value, total: totalBooks.value, percent: 100, downloadedBytes: 0, totalBytes: null });
            status.value = t("status.libraryIndexed");

            await loadAllGenres();
            await refreshSearchResults();
        } catch (err) {
            setProgress({ phase: "error", processed: 0, total: 0, percent: 0, downloadedBytes: 0, totalBytes: null });
            status.value = reindexing ? t("status.reindexFailed") : t("status.loadFailed");
            error.value = err instanceof Error ? err.message : t("error.inpxLoadFailed");
            throw err;
        } finally {
            isLoading.value = false;
            isReindexing.value = false;
        }
    }

    async function loadLibraryForCurrentMagnet() {
        if (!magnetHash.value) return;

        setProgress({ phase: "loading-cache", processed: 0, total: 0, percent: 0, downloadedBytes: 0, totalBytes: null });
        status.value = t("status.cachedLibraryRestoring");

        // Fast path: restore index + books from worker's own IDB — no book array ever on main thread
        const restoreResult = await searchWorkerClient.restoreIndex({ hash: magnetHash.value });

        if (restoreResult?.restored) {
            metadata.value = restoreResult.metadata ?? null;
            totalBooks.value = restoreResult.total ?? 0;
            lastUpdatedAt.value = restoreResult.persistedAt ? new Date(restoreResult.persistedAt).toLocaleString() : "";
            hasCache.value = true;
            setProgress({ phase: "ready", processed: totalBooks.value, total: totalBooks.value, percent: 100, downloadedBytes: 0, totalBytes: null });
            status.value = t("status.loadedFromCache");
            await loadAllGenres();
            await refreshSearchResults();
            return;
        }

        // Slow path: download + parse + build
        await fetchAndBuild();
    }

    async function submitMagnet(uri) {
        error.value = "";
        const clean = uri.trim();
        if (!clean) {
            error.value = t("error.magnetRequired");
            return;
        }

        let hash = "";
        try {
            hash = parseHashFromMagnet(clean);
        } catch (err) {
            error.value = err instanceof Error ? err.message : t("error.invalidMagnet");
            return;
        }

        magnetUri.value = clean;
        magnetHash.value = hash;
        magnetStore.set(clean);

        try {
            await loadLibraryForCurrentMagnet();
        } catch (err) {
            error.value = err instanceof Error ? err.message : t("error.inpxLoadFailed");
        }
    }

    async function submitTorrentFile(file) {
        error.value = "";

        if (!file) {
            error.value = t("error.selectTorrent");
            return;
        }

        try {
            const magnetFromFile = await convertTorrentFileToMagnet(file);
            await submitMagnet(magnetFromFile);
        } catch (err) {
            error.value = err instanceof Error ? err.message : t("error.parseTorrent");
        }
    }

    async function bootstrap() {
        await ensureGenreLabelsLoaded();

        const stored = magnetStore.get();
        if (!stored) return;

        magnetUri.value = stored;

        try {
            magnetHash.value = parseHashFromMagnet(stored);
        } catch {
            magnetStore.clear();
            magnetUri.value = "";
            magnetHash.value = "";
            resetProgress();
            return;
        }

        try {
            await loadLibraryForCurrentMagnet();
        } catch (err) {
            error.value = err instanceof Error ? err.message : t("error.savedLibraryFailed");
            status.value = t("status.savedLibraryLoadFailedTryReindex");
        }
    }

    async function reindexCurrent() {
        if (!magnetHash.value || !magnetUri.value) return;

        error.value = "";
        status.value = t("status.reindexClearing");
        setProgress({
            phase: "clearing-local",
            processed: 0,
            total: 0,
            percent: 0,
            downloadedBytes: 0,
            totalBytes: null
        });

        try {
            await searchWorkerClient.clearPersistedIndex(magnetHash.value);
            status.value = t("status.localCacheCleared");
            await fetchAndBuild({ reindexing: true });
        } catch (err) {
            error.value = err instanceof Error ? err.message : t("error.reindexFailed");
            status.value = t("status.reindexTryAgain");
        }
    }

    async function resetAll() {
        const hashToClear = magnetHash.value;

        magnetStore.clear();

        if (hashToClear) {
            await searchWorkerClient.clearPersistedIndex(hashToClear);
        }

        magnetUri.value = "";
        magnetHash.value = "";
        metadata.value = null;
        totalBooks.value = 0;
        searchMatchedBooks.value = [];
        filteredBooks.value = [];
        pagedBooks.value = [];
        selectedGenres.value = [];
        genreFacets.value = [];
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
            error.value = err instanceof Error ? err.message : t("error.downloadFailed");
        } finally {
            downloadingById[book.id] = false;
        }
    }

    ensureGenreLabelsLoaded();

    return {
        magnetUri,
        magnetHash,
        metadata,
        totalBooks,
        searchMatchedBooks,
        filteredBooks,
        pagedBooks,
        selectedGenres,
        genreFacets,
        hasGenreFilters,
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
        submitTorrentFile,
        bootstrap,
        reindexCurrent,
        resetAll,
        downloadBook,
        goToPage,
        nextPage,
        previousPage,
        toggleGenreFilter,
        clearGenreFilters,
        formatBookGenres
    };
});
