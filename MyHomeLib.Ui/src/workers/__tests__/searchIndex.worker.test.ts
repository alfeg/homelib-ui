/* eslint-disable @typescript-eslint/no-explicit-any -- worker-global stubs and loose message payloads */
// End-to-end test of the search index worker: parse-and-build → search
// (memoized query path) → restore from persisted cache. Runs the real worker
// module in Node with the `self` worker global stubbed and IndexedDB faked.
import "fake-indexeddb/auto"
import { strToU8, zipSync } from "fflate"
import { beforeAll, describe, expect, it, vi } from "vitest"

const ROW_DELIMITER = "\x04"
const HASH = "abcdef123456abcdef123456abcdef123456ab"

type WorkerMessage = { type: string; requestId?: number; payload?: any; message?: string }

let outbox: WorkerMessage[] = []

function installWorkerGlobal() {
    ;(globalThis as any).self = {
        onmessage: null as null | ((event: { data: unknown }) => Promise<void>),
        postMessage: (message: WorkerMessage) => {
            outbox.push(message)
        },
    }
}

async function importWorker() {
    installWorkerGlobal()
    await import("../searchIndex.worker")
}

async function send(data: Record<string, unknown>) {
    outbox = []
    await (globalThis as any).self.onmessage({ data })
    return outbox
}

function lastOfType(messages: WorkerMessage[], type: string) {
    return [...messages].reverse().find((m) => m.type === type)
}

// fields: 0 authors, 1 genre, 2 title, 3 series, 4 seriesNo, 5 file,
//         6 size, 7 id, 8 del, 9 ext, 10 date, 11 lang
function inpRow(id: number, title: string, genre: string, date: string) {
    return ["Фамилия,Имя,", genre, title, "Серия", "1", `${id}.fb2`, "1000", String(id), "", "fb2", date, "ru"].join(
        ROW_DELIMITER,
    )
}

function buildInpxBuffer(): ArrayBuffer {
    const rows = [
        inpRow(1, "Космос зовёт", "sf", "1995-01-01"),
        inpRow(2, "Детектив в тумане", "det", "2001-05-05"),
        inpRow(3, "Космос и звёзды", "sf", "2005-06-15"),
        inpRow(4, "Уникальнейшая повесть", "prose", "2010-03-03"),
        inpRow(5, "Книга без жанра", "", "2020-12-31"),
    ]
    const zip = zipSync({
        "fb2-000001-000005.inp": strToU8(rows.join("\n")),
        "collection.info": strToU8("Test collection"),
        "version.info": strToU8("20260706"),
    })
    return zip.buffer.slice(zip.byteOffset, zip.byteOffset + zip.byteLength) as ArrayBuffer
}

describe("searchIndex.worker", () => {
    let datasetSignature = ""

    beforeAll(async () => {
        await importWorker()
    })

    it("builds and persists the index from an INPX buffer", async () => {
        const messages = await send({
            type: "parse-and-build",
            buffer: buildInpxBuffer(),
            hash: HASH,
        })

        const complete = lastOfType(messages, "build-complete")
        expect(complete).toBeDefined()
        expect(complete!.payload.total).toBe(5)
        expect(complete!.payload.persisted).toBe(true)
        expect(complete!.payload.persistenceError).toBe("")
        datasetSignature = complete!.payload.datasetSignature
        expect(datasetSignature).toMatch(/^5:[0-9a-f]+$/)
    })

    it("returns paged results and facets for an empty term", async () => {
        const page1 = lastOfType(
            await send({ type: "search", requestId: 1, term: "", page: 1, pageSize: 2, genres: [] }),
            "search-result",
        )!
        expect(page1.payload.total).toBe(5)
        expect(page1.payload.books).toHaveLength(2)
        expect(page1.payload.yearRange).toEqual({ min: 1995, max: 2020 })

        const genreCounts = Object.fromEntries(page1.payload.genres.map((g) => [g.genre, g.count]))
        expect(genreCounts).toEqual({ sf: 2, det: 1, prose: 1, __no_genre__: 1 })

        // Page flip exercises the memoized query path.
        const page3 = lastOfType(
            await send({ type: "search", requestId: 2, term: "", page: 3, pageSize: 2, genres: [] }),
            "search-result",
        )!
        expect(page3.payload.total).toBe(5)
        expect(page3.payload.books).toHaveLength(1)
        const page1Ids = page1.payload.books.map((b) => b.id)
        expect(page1Ids).not.toContain(page3.payload.books[0].id)
    })

    it("searches by term with prefix + ё collapse", async () => {
        const result = lastOfType(
            await send({ type: "search", requestId: 3, term: "космос звезды", page: 1, pageSize: 10, genres: [] }),
            "search-result",
        )!
        expect(result.payload.total).toBe(1)
        expect(result.payload.books[0].title).toBe("Космос и звёзды")
    })

    it("applies genre and year filters", async () => {
        const byGenre = lastOfType(
            await send({ type: "search", requestId: 4, term: "", page: 1, pageSize: 10, genres: ["sf"] }),
            "search-result",
        )!
        expect(byGenre.payload.total).toBe(2)
        expect(byGenre.payload.books.map((b) => b.id).sort()).toEqual([1, 3])

        const byYear = lastOfType(
            await send({
                type: "search",
                requestId: 5,
                term: "",
                page: 1,
                pageSize: 10,
                genres: [],
                yearFrom: 2000,
                yearTo: 2010,
            }),
            "search-result",
        )!
        expect(byYear.payload.total).toBe(3)
        // Year range reflects the term-matched set, not the filtered one.
        expect(byYear.payload.yearRange).toEqual({ min: 1995, max: 2020 })
    })

    it("restores index and facets from the persisted cache in a fresh session", async () => {
        vi.resetModules()
        await importWorker()

        const messages = await send({ type: "restore", hash: HASH, signature: datasetSignature })
        const complete = lastOfType(messages, "restore-complete")
        expect(complete).toBeDefined()
        expect(complete!.payload.restored).toBe(true)
        expect(complete!.payload.total).toBe(5)

        const result = lastOfType(
            await send({ type: "search", requestId: 6, term: "уникальнейшая", page: 1, pageSize: 10, genres: [] }),
            "search-result",
        )!
        expect(result.payload.total).toBe(1)
        expect(result.payload.books[0].id).toBe(4)
        expect(result.payload.books[0].title).toBe("Уникальнейшая повесть")
    })

    it("rejects a persisted cache with a mismatched signature", async () => {
        vi.resetModules()
        await importWorker()

        const messages = await send({ type: "restore", hash: HASH, signature: "5:deadbeef" })
        const complete = lastOfType(messages, "restore-complete")
        expect(complete!.payload.restored).toBe(false)
        expect(complete!.payload.reason).toBe("stale")
    })
})
