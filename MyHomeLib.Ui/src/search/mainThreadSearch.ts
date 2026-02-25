import { Document as FlexDocument } from "flexsearch"

import type { BookRecord } from "../types/library"

const MAX_IDS = 10_000
const NO_GENRE_CODE = "__no_genre__"

let index: InstanceType<typeof FlexDocument> | null = null
let booksById = new Map<string, BookRecord>()

function normalizeSearchValue(value: unknown): string {
    return String(value ?? "")
        .toLocaleLowerCase("ru-RU")
        .replaceAll("ё", "е")
}

function toSearchText(book: BookRecord): string {
    return [book.title, book.authors, (book as any).series, book.lang, book.file]
        .filter(Boolean)
        .map(normalizeSearchValue)
        .join(" ")
}

function createIndex(): InstanceType<typeof FlexDocument> {
    return new FlexDocument({
        cache: true,
        document: {
            id: "id",
            index: [{ field: "content", tokenize: "forward", encode: false }],
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

export async function importIndexData(
    chunks: Array<{ key: string; data: unknown }>,
    books: BookRecord[],
): Promise<void> {
    const newIndex = createIndex()

    for (const chunk of chunks) {
        if (chunk.key != null) {
            await newIndex.import(chunk.key, chunk.data)
        }
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

    const normalizedTerm = normalizeSearchValue(term).trim()
    const genreFilter = genres.length ? genres : null

    let matched: BookRecord[]

    if (!normalizedTerm) {
        console.log("No search term, applying genre filter only")
        matched = []
        for (const book of booksById.values()) {
            if (genreFilter && !book.genreCodes?.some((c) => genreFilter.includes(c))) continue
            matched.push(book)
        }
    } else {
        const rawResults = index.search(normalizedTerm, { limit: MAX_IDS })
        console.log("Raw search results:", rawResults)
        const ids = extractSearchIds(rawResults)
        console.log("Extracted IDs:", ids)
        matched = ids.map((id) => booksById.get(id)).filter(Boolean) as BookRecord[]

        // Linear fallback when FlexSearch finds nothing
        if (!matched.length) {
            console.log("No matches from index, falling back to linear search")
            for (const book of booksById.values()) {
                if (toSearchText(book).includes(normalizedTerm)) {
                    matched.push(book)
                    if (matched.length >= MAX_IDS) break
                }
            }
        }

        if (genreFilter) {
            matched = matched.filter((book) => book.genreCodes?.some((c) => genreFilter.includes(c)))
        }
    }

    // Genre facets from full matched set
    const genreCounts = new Map<string, number>()
    for (const book of matched) {
        const codes = Array.isArray(book.genreCodes) && book.genreCodes.length ? book.genreCodes : [NO_GENRE_CODE]
        for (const code of codes) {
            genreCounts.set(code, (genreCounts.get(code) ?? 0) + 1)
        }
    }
    const resultGenres = Array.from(genreCounts.entries()).map(([genre, count]) => ({ genre, count }))

    const total = matched.length
    const start = (page - 1) * pageSize
    const books = matched.slice(start, start + pageSize)

    return { books, total, genres: resultGenres }
}
