import { decode } from "https://cdn.jsdelivr.net/npm/@msgpack/msgpack@3.1.3/dist.esm/index.mjs";

const jsonHeaders = {
    "Content-Type": "application/json"
};

async function requestJson(url, body) {
    const response = await fetch(url, {
        method: "POST",
        credentials: "include",
        headers: jsonHeaders,
        body: JSON.stringify(body)
    });

    if (!response.ok) {
        const errorText = await response.text();
        throw new Error(errorText || `Request failed with ${response.status}`);
    }

    return response.json();
}

function mapMetadata(metadata) {
    if (!metadata || typeof metadata !== "object") return null;

    if ("version" in metadata || "totalBooks" in metadata || "description" in metadata) {
        return metadata;
    }

    return {
        description: metadata.d ?? "",
        version: metadata.v ?? "",
        totalBooks: metadata.t ?? 0
    };
}

function mapBook(book) {
    if (!book || typeof book !== "object") return null;

    if ("archiveFile" in book || "title" in book || "authors" in book) {
        return book;
    }

    return {
        id: book.i,
        title: book.t,
        authors: book.a,
        series: book.s,
        seriesNo: book.n,
        lang: book.l,
        file: book.f,
        ext: book.e,
        archiveFile: book.r
    };
}

function mapLibraryPayload(payload) {
    if (!payload || typeof payload !== "object") {
        return { metadata: null, books: [] };
    }

    const metadataSource = payload.metadata ?? payload.m ?? null;
    const booksSource = payload.books ?? payload.b ?? [];

    return {
        metadata: mapMetadata(metadataSource),
        books: Array.isArray(booksSource)
            ? booksSource.map(mapBook).filter(Boolean)
            : []
    };
}

async function requestBooksMsgPack(body, onProgress) {
    const response = await fetch("/api/library/books/msgpack", {
        method: "POST",
        credentials: "include",
        headers: {
            ...jsonHeaders,
            Accept: "application/x-msgpack,application/msgpack"
        },
        body: JSON.stringify(body)
    });

    if (!response.ok) {
        const errorText = await response.text();
        throw new Error(errorText || `Request failed with ${response.status}`);
    }

    const totalHeader = response.headers.get("content-length");
    const parsedTotal = totalHeader ? Number.parseInt(totalHeader, 10) : NaN;
    const totalBytes = Number.isFinite(parsedTotal) && parsedTotal > 0 ? parsedTotal : null;

    if (typeof ReadableStream !== "undefined" && response.body?.getReader) {
        const reader = response.body.getReader();
        const chunks = [];
        let downloadedBytes = 0;

        while (true) {
            const { done, value } = await reader.read();
            if (done) break;
            if (!value) continue;

            chunks.push(value);
            downloadedBytes += value.byteLength;
            onProgress?.({
                downloadedBytes,
                totalBytes,
                percent: totalBytes ? Math.round((downloadedBytes / totalBytes) * 100) : null
            });
        }

        const bytes = new Uint8Array(downloadedBytes);
        let offset = 0;

        for (const chunk of chunks) {
            bytes.set(chunk, offset);
            offset += chunk.byteLength;
        }

        return mapLibraryPayload(decode(bytes));
    }

    const bytes = new Uint8Array(await response.arrayBuffer());
    onProgress?.({
        downloadedBytes: bytes.byteLength,
        totalBytes,
        percent: totalBytes ? Math.round((bytes.byteLength / totalBytes) * 100) : null
    });
    return mapLibraryPayload(decode(bytes));
}

function parseDownloadName(contentDisposition) {
    if (!contentDisposition) return "book.bin";

    const utf8Match = contentDisposition.match(/filename\*=UTF-8''([^;]+)/i);
    if (utf8Match?.[1]) return decodeURIComponent(utf8Match[1]);

    const simpleMatch = contentDisposition.match(/filename="?([^";]+)"?/i);
    return simpleMatch?.[1] ?? "book.bin";
}

export const apiClient = {
    async getUserId() {
        const response = await fetch("/api/session/user-id", {
            method: "GET",
            credentials: "include"
        });

        if (!response.ok) throw new Error("Failed to initialize session");
        return response.json();
    },

    async fetchBooks(magnetUri, forceReindex = false, onProgress) {
        const body = { magnetUri, forceReindex };

        try {
            return await requestBooksMsgPack(body, onProgress);
        } catch {
            return requestJson("/api/library/books", body);
        }
    },

    async downloadBook(payload) {
        const response = await fetch("/api/library/download", {
            method: "POST",
            credentials: "include",
            headers: jsonHeaders,
            body: JSON.stringify(payload)
        });

        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(errorText || "Failed to download file");
        }

        return {
            blob: await response.blob(),
            fileName: parseDownloadName(response.headers.get("content-disposition"))
        };
    }
};
