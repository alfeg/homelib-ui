import { unzipSync } from "https://cdn.jsdelivr.net/npm/fflate@0.8.2/esm/browser.js";

const ROW_DELIMITER = "\x04";
const utf8Decoder = new TextDecoder("utf-8");

let cp1251Decoder = null;
try {
    cp1251Decoder = new TextDecoder("windows-1251");
} catch {
    cp1251Decoder = null;
}

function decodeText(bytes) {
    if (!(bytes instanceof Uint8Array) || bytes.byteLength === 0) return "";

    if (cp1251Decoder) {
        try {
            return cp1251Decoder.decode(bytes);
        } catch {
            // ignored, fallback to utf-8
        }
    }

    try {
        return utf8Decoder.decode(bytes);
    } catch {
        return "";
    }
}

function normalizeAuthors(rawAuthors) {
    if (!rawAuthors) return "";

    const parsed = rawAuthors
        .split(":")
        .map((author) => author.trim())
        .filter(Boolean)
        .map((author) => {
            const [surname = "", firstName = "", middleName = ""] = author
                .split(",")
                .map((part) => part.trim());

            return [firstName, middleName, surname].filter(Boolean).join(" ");
        })
        .filter(Boolean);

    return parsed.join(", ");
}

function mapBook(fields, archiveFile, fallbackId) {
    const parsedId = Number.parseInt(fields[7] ?? "", 10);
    const id = Number.isFinite(parsedId) ? parsedId : fallbackId;

    return {
        id,
        title: fields[2] ?? "",
        authors: normalizeAuthors(fields[0] ?? ""),
        series: fields[3] ?? "",
        seriesNo: fields[4] ?? "",
        lang: fields[11] ?? "",
        file: fields[5] ?? "",
        ext: fields[9] ?? "",
        archiveFile
    };
}

function toArchiveFile(entryName) {
    const fileName = entryName.split("/").pop() ?? entryName;
    return fileName.replace(/\.inp$/i, ".zip");
}

self.onmessage = (event) => {
    const message = event?.data ?? {};
    if (message.type !== "parse" || !message.buffer) return;

    try {
        const archive = unzipSync(new Uint8Array(message.buffer));
        const entryNames = Object.keys(archive);

        const metadata = {
            description: "",
            version: "",
            totalBooks: 0
        };

        const books = [];
        const inpEntries = entryNames.filter((name) => name.toLowerCase().endsWith(".inp"));
        const totalEntries = inpEntries.length;

        if (archive["collection.info"]) {
            metadata.description = decodeText(archive["collection.info"]).trim();
        }

        if (archive["version.info"]) {
            metadata.version = decodeText(archive["version.info"]).trim();
        }

        self.postMessage({
            type: "progress",
            payload: {
                phase: "parsing",
                processed: 0,
                total: totalEntries,
                percent: totalEntries ? 0 : 100
            }
        });

        let fallbackId = 1;

        inpEntries.forEach((entryName, entryIndex) => {
            const text = decodeText(archive[entryName]);
            const archiveFile = toArchiveFile(entryName);
            const lines = text.split(/\r?\n/);

            lines.forEach((line) => {
                if (!line) return;

                const fields = line.split(ROW_DELIMITER);
                if (!fields.length || !fields[5]) return;

                const book = mapBook(fields, archiveFile, fallbackId);
                books.push(book);
                fallbackId += 1;
            });

            self.postMessage({
                type: "progress",
                payload: {
                    phase: "parsing",
                    processed: entryIndex + 1,
                    total: totalEntries,
                    percent: totalEntries ? Math.round(((entryIndex + 1) / totalEntries) * 100) : 100
                }
            });
        });

        metadata.totalBooks = books.length;

        self.postMessage({
            type: "result",
            payload: {
                metadata,
                books
            }
        });
    } catch (error) {
        self.postMessage({
            type: "error",
            message: error instanceof Error ? error.message : "Failed to parse INPX file."
        });
    }
};
