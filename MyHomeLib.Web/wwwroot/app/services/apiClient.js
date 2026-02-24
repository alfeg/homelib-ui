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

    fetchBooks(magnetUri, forceReindex = false) {
        return requestJson("/api/library/books", { magnetUri, forceReindex });
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