import { Document as FlexDocument } from "flexsearch"

import type { BookRecord } from "../types/library"

const MAX_IDS = 10_000
const NO_GENRE_CODE = "__no_genre__"

let index: InstanceType<typeof FlexDocument> | null = null
let booksById = new Map<string, BookRecord>()

const encodeText = (str: string): string => str.toLocaleLowerCase("ru-RU").replace(/ё/g, "е")

function createIndex(): InstanceType<typeof FlexDocument> {
    return new FlexDocument({
        cache: true,
        document: {
            id: "id",
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

/** Build the search index directly from BookRecord array (used in tests and future direct-build path). */
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

    const genreFilter = genres.length ? genres : null

    // Step 1: match by search term only (no genre filter yet)
    let termMatched: BookRecord[]

    if (!term.trim()) {
        termMatched = Array.from(booksById.values())
    } else {
        const rawResults = index.search([
            { field: "title",   query: term, limit: MAX_IDS },
            { field: "authors", query: term, limit: MAX_IDS },
            { field: "series",  query: term, limit: MAX_IDS },
            { field: "lang",    query: term, limit: MAX_IDS },
            { field: "file",    query: term, limit: MAX_IDS },
        ])
        const ids = extractSearchIds(rawResults)
        termMatched = ids.map((id) => booksById.get(id)).filter(Boolean) as BookRecord[]
    }

    // Step 2: genre facets from the pre-filter set — stable regardless of selection
    const genreCounts = new Map<string, number>()
    for (const book of termMatched) {
        const codes = Array.isArray(book.genreCodes) && book.genreCodes.length ? book.genreCodes : [NO_GENRE_CODE]
        for (const code of codes) {
            genreCounts.set(code, (genreCounts.get(code) ?? 0) + 1)
        }
    }
    const resultGenres = Array.from(genreCounts.entries()).map(([genre, count]) => ({ genre, count }))

    // Step 3: apply genre filter (OR — book matches if it belongs to ANY selected genre)
    const matched = genreFilter
        ? termMatched.filter((book) => book.genreCodes?.some((c) => genreFilter.includes(c)))
        : termMatched

    const total = matched.length
    const start = (page - 1) * pageSize
    const books = matched.slice(start, start + pageSize)

    return { books, total, genres: resultGenres }
}
