const jsonHeaders = {
    "Content-Type": "application/json"
};

async function requestArrayBuffer(url, body, onProgress) {
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

        return bytes.buffer;
    }

    const bytes = new Uint8Array(await response.arrayBuffer());
    onProgress?.({
        downloadedBytes: bytes.byteLength,
        totalBytes,
        percent: totalBytes ? Math.round((bytes.byteLength / totalBytes) * 100) : null
    });

    return bytes.buffer;
}

function parseDownloadName(contentDisposition) {
    if (!contentDisposition) return "book.bin";

    const utf8Match = contentDisposition.match(/filename\*=UTF-8''([^;]+)/i);
    if (utf8Match?.[1]) return decodeURIComponent(utf8Match[1]);

    const simpleMatch = contentDisposition.match(/filename="?([^";]+)"?/i);
    return simpleMatch?.[1] ?? "book.bin";
}

export const apiClient = {
    async fetchInpx(magnetUri, onProgress) {
        return requestArrayBuffer("/api/library/inpx", { magnetUri }, onProgress);
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
