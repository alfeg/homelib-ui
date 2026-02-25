import { Document as FlexDocument } from "flexsearch"

import type { BookRecord } from "../types/library"

const MAX_IDS = 10_000
const NO_GENRE_CODE = "__no_genre__"
const SEARCH_FIELDS = ["title", "authors", "series", "lang", "file"] as const

let index: InstanceType<typeof FlexDocument> | null = null
let booksById = new Map<string, BookRecord>()

const encodeText = (str: string): string => str.toLocaleLowerCase("ru-RU").replace(/ё/g, "е")

function createIndex(): InstanceType<typeof FlexDocument> {
    return new FlexDocument({
        document: {
            id: "id",
            tag: "genreCodes",
            index: [
                { field: "title",   tokenize: "forward", encode: encodeText },
                { field: "authors", tokenize: "forward", encode: encodeText },
                { field: "series",  tokenize: "forward", encode: encodeText },
                { field: "lang",    tokenize: "strict",  encode: encodeText },
                { field: "file",    tokenize: "forward", encode: encodeText },
            ],
        },
    })
}

function extractSearchIds(rawResults: any[]): string[] {
    const ids: string[] = []
    const seen = new Set<string>()

    for (const entry of rawResults) {
        const resultSet = Array.isArray(entry?.result) ? entry.result : []
        for (const item of resultSet) {
            const value = typeof item === "object" && item !== null ? item.id : item
            const id = String(value)
            if (!seen.has(id)) {
                seen.add(id)
                ids.push(id)
            }
        }
    }

    return ids
}

function computeFacets(books: BookRecord[]): { genre: string; count: number }[] {
    const counts = new Map<string, number>()
    for (const book of books) {
        const codes = Array.isArray(book.genreCodes) && book.genreCodes.length ? book.genreCodes : [NO_GENRE_CODE]
        for (const code of codes) {
            counts.set(code, (counts.get(code) ?? 0) + 1)
        }
    }
    return Array.from(counts.entries()).map(([genre, count]) => ({ genre, count }))
}

export async function importIndexData(chunks: Array<{ key: string; data: unknown }>, books: BookRecord[]): Promise<void> {
    const newIndex = createIndex()

    for (const chunk of chunks) {
        if (chunk.key != null) {
            await newIndex.import(chunk.key, chunk.data)
        }
    }

    booksById = new Map(books.map((b) => [String(b.id), b]))
    index = newIndex
}

/** Build the search index directly from BookRecord array (used in tests). */
export async function buildIndex(books: BookRecord[]): Promise<void> {
    const newIndex = createIndex()

    for (const book of books) {
        await newIndex.add({
            id: String(book.id),
            title: String(book.title ?? ""),
            authors: String(book.authors ?? ""),
            series: String(book.series ?? ""),
            lang: String(book.lang ?? ""),
            file: String(book.file ?? ""),
            genreCodes: Array.isArray(book.genreCodes) ? book.genreCodes : [],
        })
    }

    booksById = new Map(books.map((b) => [String(b.id), b]))
    index = newIndex
}

export function isReady(): boolean {
    return index !== null && booksById.size > 0
}

export interface SearchResult {
    books: BookRecord[]
    total: number
    genres: { genre: string; count: number }[]
}

export function search(term: string, page: number, pageSize: number, genres: string[]): SearchResult {
    if (!index) return { books: [], total: 0, genres: [] }

    const hasTerm = term.trim().length > 0
    const genreFilter = genres.length ? genres : null

    // Step 1: term-only search (no tag filter) — used for stable pre-filter facets
    let termMatched: BookRecord[]

    if (!hasTerm) {
        termMatched = Array.from(booksById.values())
    } else {
        const raw = index.search(SEARCH_FIELDS.map((field) => ({ field, query: term, limit: MAX_IDS })))
        termMatched = extractSearchIds(raw).map((id) => booksById.get(id)).filter(Boolean) as BookRecord[]
    }

    // Step 2: genre facets from pre-filter set — stable regardless of active genre selection
    const resultGenres = computeFacets(termMatched)

    // Step 3: apply genre filter via FlexSearch native tag feature (OR across selected genres)
    let matched: BookRecord[]

    if (!genreFilter) {
        matched = termMatched
    } else if (!hasTerm) {
        // Tag-only search (no text query)
        const raw = index.search({ tag: { genreCodes: genreFilter } } as any)
        matched = extractSearchIds(raw).map((id) => booksById.get(id)).filter(Boolean) as BookRecord[]
    } else {
        // Text + tag intersection — use index option form which supports top-level tag
        const raw = index.search(term, {
            index: [...SEARCH_FIELDS],
            tag: { genreCodes: genreFilter },
            limit: MAX_IDS,
        } as any)
        matched = extractSearchIds(raw).map((id) => booksById.get(id)).filter(Boolean) as BookRecord[]
    }

    const total = matched.length
    const start = (page - 1) * pageSize
    const books = matched.slice(start, start + pageSize)

    return { books, total, genres: resultGenres }
}
