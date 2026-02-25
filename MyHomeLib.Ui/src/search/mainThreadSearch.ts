import MiniSearch from "minisearch"

import type { BookRecord } from "../types/library"

const NO_GENRE_CODE = "__no_genre__"
const SEARCH_FIELDS = ["title", "authors", "series", "lang", "file"] as const

let index: MiniSearch | null = null
let booksById = new Map<string, BookRecord>()

// Normalise a single term: lowercase (Cyrillic-aware) + collapse e.
// Returning null causes MiniSearch to skip the token.
const processTerm = (term: string): string | null => {
    const normalized = term.toLocaleLowerCase("ru-RU").replace(/\u0451/g, "\u0435")
    return normalized.length > 0 ? normalized : null
}

const INDEX_OPTIONS = {
    fields: [...SEARCH_FIELDS],
    idField: "id",
    storeFields: [] as string[],
    processTerm,
}

// AND: every query token must match somewhere in the document.
// This is the core fix -- single-word OR was the root cause of thousands of
// low-relevance hits for multi-word queries like "zvezdy plamya i stal".
// BM25 then ranks the documents that match MORE tokens higher still.
const SEARCH_OPTIONS = {
    prefix: true,
    combineWith: "AND" as const,
    boost: { title: 4, authors: 2, series: 1.5, lang: 1, file: 1 },
}

function createIndex(): MiniSearch {
    return new MiniSearch(INDEX_OPTIONS)
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

/** Restore index from a MiniSearch JSON snapshot (used in production restore path). */
export function importIndexData(indexJson: string, books: BookRecord[]): void {
    index = MiniSearch.loadJSON(indexJson, INDEX_OPTIONS)
    booksById = new Map(books.map((b) => [String(b.id), b]))
}

/** Build the search index directly from BookRecord array (used in tests). */
export async function buildIndex(books: BookRecord[]): Promise<void> {
    const newIndex = createIndex()
    const seenIds = new Set<string>()
    for (const book of books) {
        const id = String(book.id)
        if (seenIds.has(id)) continue
        seenIds.add(id)
        newIndex.add({
            id,
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

    const hasTerm = term.trim().length > 0
    const genreFilter = genres.length ? genres : null

    // Step 1: text search -- BM25-ranked, AND across all tokens, prefix matching
    let termMatched: BookRecord[]

    if (!hasTerm) {
        termMatched = Array.from(booksById.values())
    } else {
        const results = index.search(term, SEARCH_OPTIONS)
        termMatched = results.map((r) => booksById.get(String(r.id))).filter(Boolean) as BookRecord[]
    }

    // Step 2: stable genre facets from the pre-filter result set
    const resultGenres = computeFacets(termMatched)

    // Step 3: genre filter -- simple JS post-filter (OR across selected genres)
    let matched: BookRecord[]

    if (!genreFilter) {
        matched = termMatched
    } else {
        matched = termMatched.filter(
            (book) => Array.isArray(book.genreCodes) && book.genreCodes.some((c) => genreFilter.includes(c)),
        )
    }

    const total = matched.length
    const start = (page - 1) * pageSize
    const books = matched.slice(start, start + pageSize)

    return { books, total, genres: resultGenres }
}
