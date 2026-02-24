const DB_NAME = "myhomelib-library-cache";
const STORE_NAME = "libraries";
const DB_VERSION = 1;

type LibraryCachePayload = {
  hash: string;
  magnetUri: string;
  metadata: Record<string, unknown> | null;
  books: any[];
  booksNormalized?: boolean;
  updatedAt?: string;
  indexMeta?: Record<string, unknown>;
};

function openDb() {
  return new Promise<IDBDatabase>((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, DB_VERSION);

    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(STORE_NAME)) {
        db.createObjectStore(STORE_NAME, { keyPath: "hash" });
      }
    };

    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error("Failed to open IndexedDB"));
  });
}

async function read(hash: string) {
  const db = await openDb();

  return new Promise<LibraryCachePayload | null>((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, "readonly");
    const request = tx.objectStore(STORE_NAME).get(hash);

    request.onsuccess = () => resolve((request.result as LibraryCachePayload | undefined) ?? null);
    request.onerror = () => reject(request.error ?? new Error("Failed to read cache"));
  });
}

async function write(record: LibraryCachePayload) {
  const db = await openDb();

  return new Promise<void>((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, "readwrite");
    tx.objectStore(STORE_NAME).put(record);

    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error ?? new Error("Failed to write cache"));
  });
}

async function remove(hash: string) {
  const db = await openDb();

  return new Promise<void>((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, "readwrite");
    tx.objectStore(STORE_NAME).delete(hash);

    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error ?? new Error("Failed to remove cache entry"));
  });
}

async function clearAll() {
  const db = await openDb();

  return new Promise<void>((resolve, reject) => {
    const tx = db.transaction(STORE_NAME, "readwrite");
    tx.objectStore(STORE_NAME).clear();

    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error ?? new Error("Failed to clear cache"));
  });
}

export const libraryCacheStore = {
  getByHash: read,
  save(record: LibraryCachePayload) {
    return write({
      ...record,
      updatedAt: new Date().toISOString()
    });
  },
  removeByHash: remove,
  clearAll
};
