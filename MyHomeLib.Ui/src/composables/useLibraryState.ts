/* eslint-disable */
import { createGlobalState } from "@vueuse/core";
import { computed, reactive, ref, watch } from "vue";
import { apiClient } from "../services/apiClient";
import { parseHashFromMagnet, magnetStore } from "../services/magnetService";
import { libraryCacheStore } from "../services/storageService";
import { convertTorrentFileToMagnet } from "../services/torrentMagnetService";
import { createSearchWorkerClient } from "../services/searchIndexWorkerClient";
import { loadGenreLabels } from "../services/genreLabelsService";

const RESULTS_PAGE_SIZE = 200;
const SEARCH_RESULTS_LIMIT = 1000;
const NO_GENRE_CODE = "__no_genre__";
const NO_GENRE_LABEL = "Без жанра";
const GENRE_DELIMITER = ":";
const INPX_PARSER_WORKER_URL = new URL("../workers/inpxParser.worker.ts", import.meta.url);

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

function updateSignatureHash(hash, value) {
    let next = hash >>> 0;

    for (let i = 0; i < value.length; i += 1) {
        next ^= value.charCodeAt(i);
        next = Math.imul(next, 16777619);
    }

    return next >>> 0;
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

function normalizeBookGenres(book) {
    if (!book || typeof book !== "object") {
        return book;
    }

    const rawGenre = typeof book.genre === "string" ? book.genre : "";
    const sourceCodes = Array.isArray(book.genreCodes)
        ? book.genreCodes
        : parseRawGenreCodes(rawGenre);

    const genreCodes = sourceCodes
        .map((code) => normalizeGenreCode(code))
        .filter(Boolean);

    return {
        ...book,
        genre: rawGenre,
        genreCodes
    };
}

function normalizeBooksGenres(input, { alreadyNormalized = false } = {}) {
    const source = Array.isArray(input) ? input : [];
    if (alreadyNormalized) {
        return source;
    }

    return source.map((book) => normalizeBookGenres(book));
}

function createCacheBooksSnapshot(input, { alreadyNormalized = false } = {}) {
    const sourceBooks = alreadyNormalized
        ? (Array.isArray(input) ? input : [])
        : normalizeBooksGenres(input);

    return sourceBooks.map((book) => {
        const genreCodes = Array.isArray(book.genreCodes)
            ? book.genreCodes
            : parseRawGenreCodes(book.genre);

        return {
            ...book,
            genre: typeof book.genre === "string" ? book.genre : "",
            genreCodes: genreCodes
                .map((code) => normalizeGenreCode(code))
                .filter(Boolean)
        };
    });
}

function bookMatchesSearchTerm(book, normalizedTerm) {
    const content = normalizeSearchText([
        book?.title,
        book?.authors,
        book?.series,
        book?.lang,
        book?.file
    ].filter(Boolean).join(" "));

    return content.includes(normalizedTerm);
}

async function findLocalSearchMatches(sourceBooks, normalizedTerm, limit) {
    const source = Array.isArray(sourceBooks) ? sourceBooks : [];
    const results = [];

    for (let i = 0; i < source.length; i += 1) {
        const book = source[i];

        if (bookMatchesSearchTerm(book, normalizedTerm)) {
            results.push(book);
            if (results.length >= limit) {
                break;
            }
        }

        if (i > 0 && i % 5000 === 0) {
            await new Promise((resolve) => setTimeout(resolve, 0));
        }
    }

    return results;
}

function computeDatasetSignature(metadata, books) {
    const sourceBooks = Array.isArray(books) ? books : [];
    const metadataSeed = metadata && typeof metadata === "object"
        ? `${metadata.title || ""}|${metadata.collection || ""}|${metadata.version || ""}`
        : "";

    let hash = 2166136261;
    hash = updateSignatureHash(hash, metadataSeed);

    for (let i = 0; i < sourceBooks.length; i += 1) {
        const book = sourceBooks[i] ?? {};
        const seed = [
            book.id,
            book.title,
            book.authors,
            book.series,
            book.lang,
            book.genre,
            Array.isArray(book.genreCodes) ? book.genreCodes.join(":") : "",
            book.file,
            book.archiveFile,
            book.ext
        ].map((value) => String(value ?? "")).join("|");

        hash = updateSignatureHash(hash, seed);
    }

    return `${sourceBooks.length}:${hash.toString(16)}`;
}

function resolveDatasetSignature(payload) {
    return payload?.indexMeta?.datasetSignature
        || computeDatasetSignature(payload?.metadata ?? null, payload?.books ?? []);
}

export const useLibraryState = createGlobalState(() => {
    const magnetUri = ref("");
    const magnetHash = ref("");
    const metadata = ref(null);
    const books = ref([]);
    const searchMatchedBooks = ref([]);
    const filteredBooks = ref([]);
    const pagedBooks = ref([]);
    const selectedGenres = ref([]);
    const genreFacets = ref([]);
    const genreBooksByCode = ref(new Map<string, any[]>());
    const genreLabelByCode = ref(new Map());
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
    const hasGenreFilters = computed(() => selectedGenres.value.length > 0);

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
    let booksNormalizedForCache = false;

    function resolveGenreLabel(genreCode) {
        if (genreCode === NO_GENRE_CODE) {
            return NO_GENRE_LABEL;
        }

        return genreLabelByCode.value.get(genreCode) ?? genreCode;
    }

    function formatBookGenres(book) {
        const codes = Array.isArray(book?.genreCodes) && book.genreCodes.length
            ? book.genreCodes
            : parseRawGenreCodes(book?.genre);

        if (!codes.length) {
            return NO_GENRE_LABEL;
        }

        return uniqueStrings(codes.map((code) => resolveGenreLabel(code))).join(", ");
    }

    function recomputeGenreFacets() {
        const facetCounts = new Map();
        const facetBooks = new Map<string, any[]>();

        for (let i = 0; i < searchMatchedBooks.value.length; i += 1) {
            const book = searchMatchedBooks.value[i];
            const codes = Array.isArray(book?.genreCodes) && book.genreCodes.length
                ? book.genreCodes
                : [NO_GENRE_CODE];

            for (let j = 0; j < codes.length; j += 1) {
                const code = codes[j];
                facetCounts.set(code, (facetCounts.get(code) ?? 0) + 1);
                const booksForCode = facetBooks.get(code);
                if (booksForCode) {
                    booksForCode.push(book);
                } else {
                    facetBooks.set(code, [book]);
                }
            }
        }

        for (let i = 0; i < selectedGenres.value.length; i += 1) {
            const code = selectedGenres.value[i];
            if (!facetCounts.has(code)) {
                facetCounts.set(code, 0);
                facetBooks.set(code, []);
            }
        }

        genreBooksByCode.value = facetBooks;

        genreFacets.value = Array.from(facetCounts.entries())
            .map(([genre, count]) => ({
                genre,
                label: resolveGenreLabel(genre),
                count
            }))
            .sort((a, b) => {
                if (b.count !== a.count) {
                    return b.count - a.count;
                }

                return a.label.localeCompare(b.label, "ru");
            });
    }

    async function ensureGenreLabelsLoaded() {
        const labels = await loadGenreLabels();
        genreLabelByCode.value = labels;
        recomputeGenreFacets();
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

    function applyGenreFilters() {
        if (!selectedGenres.value.length) {
            filteredBooks.value = searchMatchedBooks.value;
            return;
        }

        const uniqueBooks = new Map<string, any>();
        for (let i = 0; i < selectedGenres.value.length; i += 1) {
            const code = selectedGenres.value[i];
            const booksForCode = genreBooksByCode.value.get(code) ?? [];
            for (let j = 0; j < booksForCode.length; j += 1) {
                const book = booksForCode[j];
                const key = `${book?.id ?? ""}|${book?.file ?? ""}|${book?.archiveFile ?? ""}`;
                if (!uniqueBooks.has(key)) {
                    uniqueBooks.set(key, book);
                }
            }
        }

        filteredBooks.value = Array.from(uniqueBooks.values());
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

    watch(selectedGenres, () => {
        currentPage.value = 1;
        applyGenreFilters();
    }, { deep: true });

    watch(() => indexProgress.phase, async (phase) => {
        if (phase === "ready" && searchTerm.value.trim()) {
            await refreshSearchResults();
        }
    });

    async function refreshSearchResults() {
        const term = searchTerm.value.trim();
        const normalizedTerm = normalizeSearchText(term).trim();

        if (!term) {
            activeSearchRequestId += 1;
            searchMatchedBooks.value = books.value;
            recomputeGenreFacets();
            applyGenreFilters();
            return;
        }

        if (indexProgress.phase !== "ready") {
            return;
        }

        const requestId = ++searchRequestId;
        activeSearchRequestId = requestId;
        let matches = await searchWorkerClient.search(term, requestId, SEARCH_RESULTS_LIMIT);

        if (!matches.length && normalizedTerm) {
            matches = await findLocalSearchMatches(books.value, normalizedTerm, SEARCH_RESULTS_LIMIT);
        }

        if (requestId !== activeSearchRequestId) return;
        searchMatchedBooks.value = matches;
        recomputeGenreFacets();
        applyGenreFilters();
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

    async function applyBooks(payload, fromCache, { tryRestore = false } = {}) {
        metadata.value = payload.metadata ?? null;
        const parserNormalizedBooks = payload?.booksNormalized === true;
        books.value = normalizeBooksGenres(payload.books ?? [], { alreadyNormalized: parserNormalizedBooks });
        booksNormalizedForCache = true;
        searchMatchedBooks.value = searchTerm.value.trim() ? [] : books.value;
        clearGenreFilters();
        recomputeGenreFacets();
        applyGenreFilters();
        currentPage.value = 1;

        const datasetSignature = resolveDatasetSignature({
            ...payload,
            books: books.value
        });

        if (tryRestore && magnetHash.value) {
            status.value = "Cached library loaded. Restoring search index...";

            const restoreResult = await searchWorkerClient.restoreIndex({
                books: books.value,
                hash: magnetHash.value,
                signature: datasetSignature
            });

            if (restoreResult?.restored) {
                hasCache.value = fromCache;
                setProgress({
                    phase: "ready",
                    processed: books.value.length,
                    total: books.value.length,
                    percent: 100,
                    downloadedBytes: 0,
                    totalBytes: null
                });
                status.value = "Loaded from local cache.";
                await refreshSearchResults();
                return { datasetSignature, restored: true };
            }

            status.value = restoreResult?.reason === "stale"
                ? "Cached search index is stale. Rebuilding..."
                : "Cached search index unavailable. Rebuilding...";
        }

        setProgress({
            phase: "indexing",
            processed: 0,
            total: books.value.length,
            percent: 0,
            downloadedBytes: 0,
            totalBytes: null
        });
        status.value = `Building search index... 0/${books.value.length} (0%)`;

        await searchWorkerClient.buildIndex(books.value, {
            hash: magnetHash.value,
            signature: datasetSignature
        });

        hasCache.value = fromCache;
        setProgress({
            phase: "ready",
            processed: books.value.length,
            total: books.value.length,
            percent: 100,
            downloadedBytes: 0,
            totalBytes: null
        });
        status.value = fromCache ? "Loaded from local cache." : "Library indexed locally from INPX.";

        await refreshSearchResults();
        return { datasetSignature, restored: false };
    }

    async function cachePayload(hash, payload, datasetSignature) {
        const cacheBooks = createCacheBooksSnapshot(books.value, { alreadyNormalized: booksNormalizedForCache });
        const cacheMetadata = payload?.metadata && typeof payload.metadata === "object"
            ? { ...payload.metadata }
            : (payload?.metadata ?? null);

        await libraryCacheStore.save({
            hash,
            magnetUri: magnetUri.value,
            metadata: cacheMetadata,
            books: cacheBooks,
            booksNormalized: true,
            indexMeta: {
                ...(payload.indexMeta ?? {}),
                count: cacheBooks.length,
                cachedAt: new Date().toISOString(),
                datasetSignature
            }
        });
    }

    async function fetchBooksViaInpx({ reindexing = false } = {}) {
        setProgress({
            phase: "loading-backend",
            processed: 0,
            total: 0,
            percent: 0,
            downloadedBytes: 0,
            totalBytes: null
        });
        status.value = reindexing
            ? "Reindexing locally: downloading INPX from backend..."
            : "Loading library: downloading INPX from backend...";

        const inpxBuffer = await apiClient.fetchInpx(magnetUri.value, ({ downloadedBytes, totalBytes, percent }) => {
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

    async function fetchAndApply({ reindexing = false } = {}) {
        if (!magnetUri.value || !magnetHash.value) return;

        error.value = "";
        isLoading.value = !reindexing;
        isReindexing.value = reindexing;

        try {
            const payload = await fetchBooksViaInpx({ reindexing });

            status.value = "Library data loaded. Building search index...";
            const { datasetSignature } = await applyBooks(payload, false);
            await cachePayload(magnetHash.value, payload, datasetSignature);
            lastUpdatedAt.value = new Date().toLocaleString();
        } catch (err) {
            setProgress({
                phase: "error",
                processed: 0,
                total: 0,
                percent: 0,
                downloadedBytes: 0,
                totalBytes: null
            });
            status.value = reindexing
                ? "Local reindex failed: unable to download or parse INPX."
                : "Library load failed: unable to download or parse INPX.";
            error.value = err instanceof Error
                ? err.message
                : "Failed to load library from INPX.";
            throw err;
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
            await applyBooks(cached, true, { tryRestore: true });
            lastUpdatedAt.value = cached.updatedAt ? new Date(cached.updatedAt).toLocaleString() : "";
            return;
        }

        await fetchAndApply();
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

    async function submitTorrentFile(file) {
        error.value = "";

        if (!file) {
            error.value = "Please choose a .torrent file.";
            return;
        }

        try {
            const magnetFromFile = await convertTorrentFileToMagnet(file);
            await submitMagnet(magnetFromFile);
        } catch (err) {
            error.value = err instanceof Error ? err.message : "Failed to parse .torrent file.";
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
            error.value = err instanceof Error ? err.message : "Failed to load saved library.";
            status.value = "Failed to load saved library. Please try reindexing.";
        }
    }

    async function reindexCurrent() {
        if (!magnetHash.value || !magnetUri.value) return;

        error.value = "";
        status.value = "Reindexing locally: clearing cached library and search index...";
        setProgress({
            phase: "clearing-local",
            processed: 0,
            total: 0,
            percent: 0,
            downloadedBytes: 0,
            totalBytes: null
        });

        try {
            await Promise.all([
                libraryCacheStore.removeByHash(magnetHash.value),
                searchWorkerClient.clearPersistedIndex(magnetHash.value)
            ]);

            status.value = "Local cache cleared. Downloading INPX for rebuild...";
            await fetchAndApply({ reindexing: true });
        } catch (err) {
            error.value = err instanceof Error ? err.message : "Failed to reindex.";
            status.value = "Local reindex failed. Please try again.";
        }
    }

    async function resetAll() {
        const hashToClear = magnetHash.value;

        magnetStore.clear();
        await libraryCacheStore.clearAll();

        if (hashToClear) {
            await searchWorkerClient.clearPersistedIndex(hashToClear);
        }

        magnetUri.value = "";
        magnetHash.value = "";
        metadata.value = null;
        books.value = [];
        booksNormalizedForCache = false;
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
            error.value = err instanceof Error ? err.message : "Download failed.";
        } finally {
            downloadingById[book.id] = false;
        }
    }

    ensureGenreLabelsLoaded();

    return {
        magnetUri,
        magnetHash,
        metadata,
        books,
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
