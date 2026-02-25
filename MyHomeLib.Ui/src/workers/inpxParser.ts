import { unzipSync } from "fflate"

const ROW_DELIMITER = "\x04"
const GENRE_DELIMITER = ":"
const utf8Decoder = new TextDecoder("utf-8")

let cp1251Decoder: TextDecoder | null = null
try {
    cp1251Decoder = new TextDecoder("windows-1251")
} catch {
    cp1251Decoder = null
}

export interface ParsedBook {
    id: number
    title: string
    genre: string
    genreCodes: string[]
    authors: string
    series: string
    seriesNo: string
    lang: string
    file: string
    ext: string
    archiveFile: string
}

export interface ParsedMetadata {
    description: string
    version: string
    totalBooks: number
}

export interface ParseResult {
    metadata: ParsedMetadata
    books: ParsedBook[]
    datasetSignature: string
}

export type ParseProgressCallback = (phase: string, processed: number, total: number, percent: number) => void

function decodeText(bytes: Uint8Array): string {
    if (!(bytes instanceof Uint8Array) || bytes.byteLength === 0) return ""

    const looksLikeMojibake = (text: string) => {
        if (!text) return false
        const artifacts = text.match(/[РС][\u0080-\u00bf]/g)
        if (!artifacts || artifacts.length === 0) return false
        return artifacts.length / text.length > 0.01
    }

    try {
        const utf8Text = utf8Decoder.decode(bytes)
        if (!looksLikeMojibake(utf8Text)) return utf8Text
    } catch {
        /* ignored */
    }

    if (cp1251Decoder) {
        try {
            return cp1251Decoder.decode(bytes)
        } catch {
            /* ignored */
        }
    }

    try {
        return utf8Decoder.decode(bytes)
    } catch {
        return ""
    }
}

function normalizeAuthors(rawAuthors: string): string {
    if (!rawAuthors) return ""
    return rawAuthors
        .split(":")
        .map((a) => a.trim())
        .filter(Boolean)
        .map((a) => {
            const [surname = "", firstName = "", middleName = ""] = a.split(",").map((p) => p.trim())
            return [firstName, middleName, surname].filter(Boolean).join(" ")
        })
        .filter(Boolean)
        .join(", ")
}

function normalizeString(value: unknown): string {
    return typeof value === "string" ? value : String(value ?? "")
}

function normalizeId(value: unknown, fallbackId: number): number {
    const parsed = Number.parseInt(normalizeString(value), 10)
    return Number.isFinite(parsed) ? parsed : fallbackId
}

function normalizeGenreCodes(rawGenre: string): string[] {
    return normalizeString(rawGenre)
        .split(GENRE_DELIMITER)
        .map((c) => normalizeString(c).trim())
        .filter(Boolean)
}

function mapBook(fields: string[], archiveFile: string, fallbackId: number): ParsedBook {
    const rawGenre = normalizeString(fields[1])
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
        archiveFile: normalizeString(archiveFile),
    }
}

function toArchiveFile(entryName: string): string {
    const fileName = entryName.split("/").pop() ?? entryName
    return fileName.replace(/\.inp$/i, ".zip")
}

function updateSigHash(hash: number, value: string): number {
    let h = hash >>> 0
    for (let i = 0; i < value.length; i++) {
        h ^= value.charCodeAt(i)
        h = Math.imul(h, 16777619)
    }
    return h >>> 0
}

export function parseInpxBuffer(buffer: ArrayBuffer, onProgress?: ParseProgressCallback): ParseResult {
    const archive = unzipSync(new Uint8Array(buffer))
    const entryNames = Object.keys(archive)
    const metadata: ParsedMetadata = { description: "", version: "", totalBooks: 0 }
    const inpEntries = entryNames.filter((n) => n.toLowerCase().endsWith(".inp"))
    const totalEntries = inpEntries.length

    if (archive["collection.info"]) {
        metadata.description = decodeText(archive["collection.info"]).trim()
    }
    if (archive["version.info"]) {
        metadata.version = decodeText(archive["version.info"]).trim()
    }

    let sigHash = updateSigHash(2166136261, `${metadata.description}|${metadata.version}`)

    onProgress?.("parsing", 0, totalEntries, totalEntries ? 0 : 100)

    const books: ParsedBook[] = []
    let fallbackId = 1

    for (let ei = 0; ei < inpEntries.length; ei++) {
        const entryName = inpEntries[ei]
        const text = decodeText(archive[entryName])
        const archiveFile = toArchiveFile(entryName)
        const lines = text.split(/\r?\n/)

        for (const line of lines) {
            if (!line) continue
            const fields = line.split(ROW_DELIMITER)
            if (!fields.length || !fields[5]) continue

            const book = mapBook(fields, archiveFile, fallbackId++)
            books.push(book)
            sigHash = updateSigHash(
                sigHash,
                `${book.id}|${book.title}|${book.authors}|${book.series}|${book.lang}|${book.genre}|${book.file}|${book.archiveFile}|${book.ext}`,
            )
        }

        const pct = totalEntries ? Math.round(((ei + 1) / totalEntries) * 100) : 100
        onProgress?.("parsing", ei + 1, totalEntries, pct)
    }

    metadata.totalBooks = books.length

    return {
        metadata,
        books,
        datasetSignature: `${books.length}:${sigHash.toString(16)}`,
    }
}
