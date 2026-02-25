import { beforeAll, describe, expect, it } from "vitest"

import type { BookRecord } from "../../types/library"
import { buildIndex, search } from "../mainThreadSearch"

function book(overrides: Partial<BookRecord> & { id: number | string }): BookRecord {
    return {
        title: "",
        genre: "",
        genreCodes: [],
        authors: "",
        series: "",
        seriesNo: "",
        lang: "ru",
        file: "",
        ext: "fb2",
        archiveFile: "lib.zip",
        ...overrides,
    }
}

// ---------------------------------------------------------------------------
// Shared fixture
// ---------------------------------------------------------------------------

const BOOKS: BookRecord[] = [
    book({ id: 1, title: "Первый закон", authors: "Джо Аберкромби", series: "Первый закон", lang: "ru", file: "1", genreCodes: ["sf_fantasy"] }),
    book({ id: 2, title: "Война и мир", authors: "Лев Толстой", series: "", lang: "ru", file: "2", genreCodes: ["prose_rus_classic"] }),
    book({ id: 3, title: "The Fellowship of the Ring", authors: "J.R.R. Tolkien", series: "Lord of the Rings", lang: "en", file: "3", genreCodes: ["sf_fantasy"] }),
    book({ id: 4, title: "Ёжик в тумане", authors: "Сергей Козлов", series: "", lang: "ru", file: "4", genreCodes: ["child_tale"] }),
    book({ id: 5, title: "Zwanzig Briefe", authors: "Karl Mayer", series: "Briefe", lang: "de", file: "5", genreCodes: ["prose_contemporary"] }),
    book({ id: 6, title: "Первый снег", authors: "Анна Иванова", series: "Времена года", lang: "ru", file: "6", genreCodes: ["prose_contemporary"] }),
]

beforeAll(async () => {
    await buildIndex(BOOKS)
})

// ---------------------------------------------------------------------------
// Cyrillic encoding
// ---------------------------------------------------------------------------

describe("Cyrillic encoding", () => {
    it("finds by lowercase Cyrillic term", () => {
        const { books } = search("первый", 1, 100, [])
        expect(books.map((b) => b.id)).toContain(1)
        expect(books.map((b) => b.id)).toContain(6)
    })

    it("finds by uppercase Cyrillic term", () => {
        const { books } = search("ПЕРВЫЙ", 1, 100, [])
        expect(books.map((b) => b.id)).toContain(1)
        expect(books.map((b) => b.id)).toContain(6)
    })

    it("ё is treated as е — searching ежик finds ёжик", () => {
        const { books } = search("ежик", 1, 100, [])
        expect(books.map((b) => b.id)).toContain(4)
    })

    it("ё literal also finds ёжик", () => {
        const { books } = search("ёжик", 1, 100, [])
        expect(books.map((b) => b.id)).toContain(4)
    })
})

// ---------------------------------------------------------------------------
// Prefix / forward tokenize
// ---------------------------------------------------------------------------

describe("Prefix (forward tokenize) search", () => {
    it("finds by title prefix", () => {
        const { books } = search("перв", 1, 100, [])
        const ids = books.map((b) => b.id)
        expect(ids).toContain(1)
        expect(ids).toContain(6)
    })

    it("finds by author prefix", () => {
        const { books } = search("аберкром", 1, 100, [])
        expect(books.map((b) => b.id)).toContain(1)
    })

    it("finds by series prefix", () => {
        const { books } = search("времена", 1, 100, [])
        expect(books.map((b) => b.id)).toContain(6)
    })

    it("full word match works", () => {
        const { books } = search("закон", 1, 100, [])
        expect(books.map((b) => b.id)).toContain(1)
    })
})

// ---------------------------------------------------------------------------
// Multi-field search
// ---------------------------------------------------------------------------

describe("Multi-field search", () => {
    it("finds by title", () => {
        const { books } = search("война", 1, 100, [])
        expect(books.map((b) => b.id)).toContain(2)
    })

    it("finds by author", () => {
        const { books } = search("толстой", 1, 100, [])
        expect(books.map((b) => b.id)).toContain(2)
    })

    it("finds English title", () => {
        const { books } = search("fellowship", 1, 100, [])
        expect(books.map((b) => b.id)).toContain(3)
    })

    it("finds English author", () => {
        const { books } = search("tolkien", 1, 100, [])
        expect(books.map((b) => b.id)).toContain(3)
    })

    it("title matches rank before author matches", () => {
        // "первый" is in title of 1 and 6, and in series of 1
        const { books } = search("первый", 1, 100, [])
        const ids = books.map((b) => b.id)
        expect(ids).toContain(1)
        expect(ids).toContain(6)
        // Title hits should come before any non-title hit
        const firstNonTitle = ids.find((id) => ![1, 6].includes(id as number))
        if (firstNonTitle !== undefined) {
            expect(ids.indexOf(1)).toBeLessThan(ids.indexOf(firstNonTitle))
        }
    })

    it("returns no results for unknown term", () => {
        const { books, total } = search("xyzxyzxyz", 1, 100, [])
        expect(books).toHaveLength(0)
        expect(total).toBe(0)
    })
})

// ---------------------------------------------------------------------------
// Language field (strict tokenize)
// ---------------------------------------------------------------------------

describe("Language field (strict tokenize)", () => {
    // Book 5 uses lang:"de" and has no "de" prefix anywhere in title/authors/series
    // so results are driven by the lang field only.
    it("finds a book via its language code", () => {
        const { books } = search("de", 1, 100, [])
        expect(books.map((b) => b.id)).toContain(5)
    })

    it("lang:de result does not include books with unrelated language", () => {
        const { books } = search("de", 1, 100, [])
        // "ru"-language-only books with no "de" in any other field should not appear
        expect(books.map((b) => b.id)).not.toContain(2) // "Война и мир", lang:ru
        expect(books.map((b) => b.id)).not.toContain(4) // "Ёжик в тумане", lang:ru
    })
})

// ---------------------------------------------------------------------------
// Genre filtering (OR logic)
// ---------------------------------------------------------------------------

describe("Genre filtering (OR)", () => {
    it("filters results to the given genre", () => {
        const { books } = search("первый", 1, 100, ["sf_fantasy"])
        const ids = books.map((b) => b.id)
        expect(ids).toContain(1)            // sf_fantasy
        expect(ids).not.toContain(6)        // prose_contemporary
    })

    it("empty genres array = no filter", () => {
        const { books } = search("первый", 1, 100, [])
        expect(books.length).toBeGreaterThanOrEqual(2)
    })

    it("multiple genres = OR: book matching any selected genre is included", () => {
        // id=1 is sf_fantasy, id=6 is prose_contemporary — selecting both should return both
        const { books } = search("первый", 1, 100, ["sf_fantasy", "prose_contemporary"])
        const ids = books.map((b) => b.id)
        expect(ids).toContain(1)
        expect(ids).toContain(6)
    })

    it("genre filter on empty term returns only matching books", () => {
        const { books } = search("", 1, 100, ["child_tale"])
        expect(books.every((b) => b.genreCodes.includes("child_tale"))).toBe(true)
    })
})

// ---------------------------------------------------------------------------
// Pagination
// ---------------------------------------------------------------------------

describe("Pagination", () => {
    it("pageSize limits returned books", () => {
        const { books, total } = search("", 1, 2, [])
        expect(books).toHaveLength(2)
        expect(total).toBe(BOOKS.length)
    })

    it("page 2 returns next slice with no overlap", () => {
        const page1 = search("", 1, 2, []).books.map((b) => b.id)
        const page2 = search("", 2, 2, []).books.map((b) => b.id)
        expect(page2).toHaveLength(2)
        page2.forEach((id) => expect(page1).not.toContain(id))
    })

    it("last page may be smaller than pageSize", () => {
        // 6 books, pageSize=4 → page 2 has 2
        const { books, total } = search("", 2, 4, [])
        expect(books).toHaveLength(2)
        expect(total).toBe(6)
    })

    it("total reflects genre-filtered count, not page size", () => {
        const { total } = search("первый", 1, 1, [])
        expect(total).toBeGreaterThanOrEqual(2) // ids 1 and 6 both match
    })
})

// ---------------------------------------------------------------------------
// Genre facets — pre-filter (stable counts)
// ---------------------------------------------------------------------------

describe("Genre facets", () => {
    it("returns genre counts for all term-matched books", () => {
        const { genres } = search("первый", 1, 1, [])
        const byGenre = Object.fromEntries(genres.map(({ genre, count }) => [genre, count]))
        // id=1 → sf_fantasy, id=6 → prose_contemporary
        expect(byGenre["sf_fantasy"]).toBeGreaterThanOrEqual(1)
        expect(byGenre["prose_contemporary"]).toBeGreaterThanOrEqual(1)
    })

    it("facets are stable — same regardless of active genre filter", () => {
        const unfiltered = search("первый", 1, 100, []).genres
        const filtered   = search("первый", 1, 100, ["sf_fantasy"]).genres
        // Counts come from the pre-filter set, so they must be identical
        expect(filtered).toEqual(unfiltered)
    })

    it("facets are stable regardless of page", () => {
        const page1 = search("первый", 1, 1, []).genres
        const full  = search("первый", 1, 100, []).genres
        expect(page1).toEqual(full)
    })

    it("total filtered books changes with genre filter, but facet counts do not", () => {
        const { total: totalAll, genres: genresAll } = search("первый", 1, 100, [])
        const { total: totalSf,  genres: genresSf  } = search("первый", 1, 100, ["sf_fantasy"])
        expect(totalSf).toBeLessThan(totalAll)  // fewer books after genre filter
        expect(genresSf).toEqual(genresAll)      // but genre counts unchanged
    })

    it("facets sum covers all term-matched books across all genres", () => {
        const { genres } = search("", 1, 100, [])
        const facetTotal = genres.reduce((sum, g) => sum + g.count, 0)
        // Each book contributes one count per genre code; sum >= number of books
        expect(facetTotal).toBeGreaterThanOrEqual(BOOKS.length)
    })
})

// ---------------------------------------------------------------------------
// Empty term
// ---------------------------------------------------------------------------

describe("Empty term", () => {
    it("returns all books when term is empty", () => {
        const { total } = search("", 1, 100, [])
        expect(total).toBe(BOOKS.length)
    })

    it("whitespace-only term treated as empty", () => {
        const { total } = search("   ", 1, 100, [])
        expect(total).toBe(BOOKS.length)
    })
})

// ---------------------------------------------------------------------------
// Re-index (buildIndex replaces previous state)
// ---------------------------------------------------------------------------

describe("buildIndex replaces previous state", () => {
    it("after re-index with subset, old books are gone", async () => {
        const subset = BOOKS.slice(0, 2)
        await buildIndex(subset)

        const { total } = search("", 1, 100, [])
        expect(total).toBe(2)

        // Restore full index for any tests that might follow
        await buildIndex(BOOKS)
    })
})
