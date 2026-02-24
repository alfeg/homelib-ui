const DB_NAME = "myhomelib-library-cache";
const STORE_NAME = "libraries";
const DB_VERSION = 1;
const MAX_LOGGED_ISSUES = 10;

function openDb() {
    return new Promise((resolve, reject) => {
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

function isPlainObject(value) {
    if (!value || typeof value !== "object") return false;

    const prototype = Object.getPrototypeOf(value);
    return prototype === Object.prototype || prototype === null;
}

function tryFastCloneSafeRecord(value) {
    if (value === null) {
        return { ok: true, normalized: null };
    }

    const valueType = typeof value;
    if (valueType === "string" || valueType === "number" || valueType === "boolean") {
        return { ok: true, normalized: value };
    }

    if (valueType !== "object" || (!Array.isArray(value) && !isPlainObject(value))) {
        return { ok: false };
    }

    const stack = [value];
    const seen = new WeakSet();

    while (stack.length) {
        const current = stack.pop();

        if (current === null || typeof current !== "object") {
            continue;
        }

        if (seen.has(current)) {
            return { ok: false };
        }

        seen.add(current);

        if (Array.isArray(current)) {
            for (let i = 0; i < current.length; i += 1) {
                const item = current[i];
                const itemType = typeof item;

                if (item === null || itemType === "string" || itemType === "number" || itemType === "boolean") {
                    continue;
                }

                if (itemType === "object" && (Array.isArray(item) || isPlainObject(item))) {
                    stack.push(item);
                    continue;
                }

                return { ok: false };
            }

            continue;
        }

        if (!isPlainObject(current)) {
            return { ok: false };
        }

        const keys = Object.keys(current);
        for (let i = 0; i < keys.length; i += 1) {
            const key = keys[i];
            const item = current[key];
            const itemType = typeof item;

            if (item === null || itemType === "string" || itemType === "number" || itemType === "boolean") {
                continue;
            }

            if (itemType === "object" && (Array.isArray(item) || isPlainObject(item))) {
                stack.push(item);
                continue;
            }

            return { ok: false };
        }
    }

    return { ok: true, normalized: value };
}

function normalizeForIndexedDb(value, path, issues, seen) {
    if (value === null) return null;

    const valueType = typeof value;

    if (valueType === "string" || valueType === "number" || valueType === "boolean") {
        return value;
    }

    if (valueType === "undefined") {
        issues.push({ path, reason: "undefined value" });
        return null;
    }

    if (valueType === "bigint") {
        issues.push({ path, reason: "bigint converted to string" });
        return value.toString();
    }

    if (valueType === "function" || valueType === "symbol") {
        issues.push({ path, reason: `unsupported type: ${valueType}` });
        return null;
    }

    if (value instanceof Date) {
        if (Number.isNaN(value.getTime())) {
            issues.push({ path, reason: "invalid date" });
            return null;
        }

        return value.toISOString();
    }

    if (Array.isArray(value)) {
        if (seen.has(value)) {
            issues.push({ path, reason: "circular array reference" });
            return [];
        }

        seen.add(value);
        const normalizedArray = value.map((item, index) => normalizeForIndexedDb(item, `${path}[${index}]`, issues, seen));
        seen.delete(value);
        return normalizedArray;
    }

    if (value instanceof Map) {
        issues.push({ path, reason: "map converted to plain object" });
        const plainObject = {};

        for (const [key, mapValue] of value.entries()) {
            const normalizedKey = String(key);
            plainObject[normalizedKey] = normalizeForIndexedDb(mapValue, `${path}.${normalizedKey}`, issues, seen);
        }

        return plainObject;
    }

    if (value instanceof Set) {
        issues.push({ path, reason: "set converted to array" });
        return Array.from(value, (item, index) => normalizeForIndexedDb(item, `${path}[${index}]`, issues, seen));
    }

    if (ArrayBuffer.isView(value)) {
        issues.push({ path, reason: "typed array converted to number array" });
        return Array.from(value);
    }

    if (value instanceof ArrayBuffer) {
        issues.push({ path, reason: "array buffer converted to number array" });
        return Array.from(new Uint8Array(value));
    }

    if (valueType === "object") {
        if (seen.has(value)) {
            issues.push({ path, reason: "circular object reference" });
            return null;
        }

        seen.add(value);

        const normalizedObject = {};
        const keys = Object.keys(value);

        for (let i = 0; i < keys.length; i += 1) {
            const key = keys[i];
            normalizedObject[key] = normalizeForIndexedDb(value[key], `${path}.${key}`, issues, seen);
        }

        seen.delete(value);
        return normalizedObject;
    }

    issues.push({ path, reason: `unsupported value: ${Object.prototype.toString.call(value)}` });
    return null;
}

function normalizeRecordForStorage(record) {
    const fastPath = tryFastCloneSafeRecord(record);
    if (fastPath.ok) {
        return { normalized: fastPath.normalized, issues: [] };
    }

    const issues = [];
    const normalized = normalizeForIndexedDb(record, "$", issues, new WeakSet());

    if (issues.length > 0) {
        console.warn("[libraryCacheStore] Normalized cache payload before IndexedDB save.", {
            issueCount: issues.length,
            issues: issues.slice(0, MAX_LOGGED_ISSUES)
        });
    }

    return { normalized, issues };
}

async function read(hash) {
    const db = await openDb();

    return new Promise((resolve, reject) => {
        const tx = db.transaction(STORE_NAME, "readonly");
        const request = tx.objectStore(STORE_NAME).get(hash);

        request.onsuccess = () => resolve(request.result ?? null);
        request.onerror = () => reject(request.error ?? new Error("Failed to read cache"));
    });
}

async function write(record) {
    const db = await openDb();
    const { normalized, issues } = normalizeRecordForStorage(record);

    return new Promise((resolve, reject) => {
        const tx = db.transaction(STORE_NAME, "readwrite");
        const request = tx.objectStore(STORE_NAME).put(normalized);

        request.onerror = () => {
            console.error("[libraryCacheStore] IndexedDB save failed.", {
                hash: normalized?.hash ?? null,
                reason: request.error?.message ?? "unknown",
                firstIssue: issues[0] ?? null
            });
        };

        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error ?? request.error ?? new Error("Failed to write cache"));
    });
}

async function remove(hash) {
    const db = await openDb();

    return new Promise((resolve, reject) => {
        const tx = db.transaction(STORE_NAME, "readwrite");
        tx.objectStore(STORE_NAME).delete(hash);

        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error ?? new Error("Failed to remove cache entry"));
    });
}

async function clearAll() {
    const db = await openDb();

    return new Promise((resolve, reject) => {
        const tx = db.transaction(STORE_NAME, "readwrite");
        tx.objectStore(STORE_NAME).clear();

        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error ?? new Error("Failed to clear cache"));
    });
}

export const libraryCacheStore = {
    getByHash: read,
    save(record) {
        return write({
            ...record,
            updatedAt: new Date().toISOString()
        });
    },
    removeByHash: remove,
    clearAll
};
