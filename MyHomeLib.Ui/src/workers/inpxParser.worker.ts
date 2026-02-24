import { unzipSync } from "fflate";

const ROW_DELIMITER = "\x04";
const GENRE_DELIMITER = ":";
const utf8Decoder = new TextDecoder("utf-8");

let cp1251Decoder = null;
try {
    cp1251Decoder = new TextDecoder("windows-1251");
} catch {
    cp1251Decoder = null;
}

function decodeText(bytes) {
    if (!(bytes instanceof Uint8Array) || bytes.byteLength === 0) return "";

    const looksLikeMojibake = (text) => {
        if (!text) return false;

        const artifacts = text.match(/[РС][\u0080-\u00bf]/g);
        if (!artifacts || artifacts.length === 0) return false;

        return artifacts.length / text.length > 0.01;
    };

    try {
        const utf8Text = utf8Decoder.decode(bytes);
        if (!looksLikeMojibake(utf8Text)) {
            return utf8Text;
        }
    } catch {
        // ignored, fallback to windows-1251
    }

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

function normalizeString(value) {
    return typeof value === "string" ? value : String(value ?? "");
}

function normalizeId(value, fallbackId) {
    const parsedId = Number.parseInt(normalizeString(value), 10);
    return Number.isFinite(parsedId) ? parsedId : fallbackId;
}

function normalizeGenreCodes(rawGenre) {
    return normalizeString(rawGenre)
        .split(GENRE_DELIMITER)
        .map((code) => normalizeString(code).trim())
        .filter(Boolean);
}

function mapBook(fields, archiveFile, fallbackId) {
    const rawGenre = normalizeString(fields[1]);

    return {
        id: normalizeId(fields[7], fallbackId),
        title: normalizeString(fields[2]),
        genre: rawGenre,
        genreCodes: normalizeGenreCodes(rawGenre),
        authors: normalizeAuthors(normalizeString(fields[0])),
        series: normalizeString(fields[3]),
        seriesNo: normalizeString(fields[4]),
        lang: normalizeString(fields[11]),
        file: normalizeString(fields[5]),
        ext: normalizeString(fields[9]),
        archiveFile: normalizeString(archiveFile)
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
                books,
                booksNormalized: true
            }
        });
    } catch (error) {
        self.postMessage({
            type: "error",
            message: error instanceof Error ? error.message : "Failed to parse INPX file."
        });
    }
};
