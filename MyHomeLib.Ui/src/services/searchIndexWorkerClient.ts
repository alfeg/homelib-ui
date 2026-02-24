import type { BookRecord } from "../types/library";

const SEARCH_INDEX_WORKER_URL = new URL("../workers/searchIndex.worker.ts", import.meta.url);

function toStructuredCloneableBooks(books: BookRecord[]) {
  const input = Array.isArray(books) ? books : [];

  try {
    return JSON.parse(JSON.stringify(input));
  } catch {
    return input.map((book) => {
      if (!book || typeof book !== "object") {
        return book;
      }

      try {
        return JSON.parse(JSON.stringify(book));
      } catch {
        return {
          id: book.id,
          title: book.title,
          authors: book.authors,
          series: book.series,
          lang: book.lang,
          file: book.file
        };
      }
    });
  }
}

export function createSearchWorkerClient({ onProgress, onError }: { onProgress?: (payload: any) => void; onError?: (error: Error) => void } = {}) {
  const worker = new Worker(SEARCH_INDEX_WORKER_URL, { type: "module" });
  const pendingSearches = new Map<number, (books: BookRecord[]) => void>();
  let pendingBuild: { resolve: (value: any) => void; reject: (reason?: unknown) => void } | null = null;
  let pendingRestore: { resolve: (value: any) => void; reject: (reason?: unknown) => void } | null = null;
  let pendingClear: { resolve: (value: any) => void; reject: (reason?: unknown) => void } | null = null;

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

      pendingBuild?.resolve(message.payload ?? { total: 0, persisted: false });
      pendingBuild = null;
      return;
    }

    if (message.type === "restore-complete") {
      pendingRestore?.resolve(message.payload ?? { restored: false, reason: "unknown" });
      pendingRestore = null;
      return;
    }

    if (message.type === "clear-persisted-complete") {
      pendingClear?.resolve(message.payload ?? { cleared: false, reason: "unknown" });
      pendingClear = null;
      return;
    }

    if (message.type === "search-result") {
      const requestId = Number(message.payload?.requestId);
      if (!pendingSearches.has(requestId)) return;

      const resolve = pendingSearches.get(requestId)!;
      pendingSearches.delete(requestId);
      resolve(message.payload?.books ?? []);
    }
  };

  worker.onerror = (event) => {
    const err = new Error(event?.message || "Search worker failed.");

    pendingBuild?.reject(err);
    pendingBuild = null;

    pendingRestore?.reject(err);
    pendingRestore = null;

    pendingClear?.reject(err);
    pendingClear = null;

    pendingSearches.forEach((resolve) => resolve([]));
    pendingSearches.clear();

    onError?.(err);
  };

  return {
    buildIndex(books: BookRecord[], { hash = "", signature = "", batchSize }: { hash?: string; signature?: string; batchSize?: number } = {}) {
      if (pendingBuild) {
        pendingBuild.reject(new Error("Index build interrupted by a new build request."));
      }

      return new Promise((resolve, reject) => {
        const cloneableBooks = toStructuredCloneableBooks(books);
        pendingBuild = { resolve, reject };
        worker.postMessage({ type: "build", books: cloneableBooks, hash, signature, batchSize });
      });
    },
    restoreIndex({ books, hash = "", signature = "" }: { books: BookRecord[]; hash?: string; signature?: string }) {
      if (pendingRestore) {
        pendingRestore.reject(new Error("Index restore interrupted by a new restore request."));
      }

      return new Promise((resolve, reject) => {
        const cloneableBooks = toStructuredCloneableBooks(books);
        pendingRestore = { resolve, reject };
        worker.postMessage({ type: "restore", books: cloneableBooks, hash, signature });
      });
    },
    clearPersistedIndex(hash: string) {
      if (pendingClear) {
        pendingClear.reject(new Error("Persisted index clear interrupted by a new clear request."));
      }

      return new Promise((resolve, reject) => {
        pendingClear = { resolve, reject };
        worker.postMessage({ type: "clear-persisted", hash });
      });
    },
    search(term: string, requestId: number, limit = 1000) {
      return new Promise<BookRecord[]>((resolve) => {
        pendingSearches.set(requestId, resolve);
        worker.postMessage({ type: "search", term, requestId, limit });
      });
    }
  };
}
