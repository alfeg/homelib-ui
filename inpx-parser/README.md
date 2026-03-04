# inpx-parser

Parser for **INPX** e-book library archives used by [MyHomeLib](https://github.com/MyHomeLibrary/MyHomeLib).

An INPX file is a ZIP archive containing pipe-delimited `.inp` catalog files, one per book archive, plus optional `collection.info` and `version.info` metadata files.

## Install

```sh
npm install inpx-parser
```

## API

```ts
function parseInpxBuffer(
    buffer: ArrayBuffer,
    onProgress?: ParseProgressCallback
): ParseResult

function parseInpxBufferStreaming(
    buffer: ArrayBuffer,
    onBooksBatch: ParseBooksBatchCallback,
    onProgress?: ParseProgressCallback,
    batchSize?: number
): Promise<StreamingParseResult>
```

### Types

```ts
interface ParsedBook {
    id: number
    title: string
    genre: string          // raw genre string, e.g. "sf:det_action"
    genreCodes: string[]   // split genre codes, e.g. ["sf", "det_action"]
    authors: string        // normalized, e.g. "Isaac Asimov, Arthur Clarke"
    series: string
    seriesNo: string
    lang: string           // e.g. "ru", "en"
    file: string           // file name inside the ZIP archive
    ext: string            // e.g. "fb2", "epub"
    archiveFile: string    // ZIP archive name, e.g. "fb2-000001-025000.zip"
    date: string           // added date
}

interface ParsedMetadata {
    description: string
    version: string
    totalBooks: number
}

interface ParseResult {
    metadata: ParsedMetadata
    books: ParsedBook[]
    datasetSignature: string  // content hash, useful for cache invalidation
}

interface StreamingParseResult {
    metadata: ParsedMetadata
    datasetSignature: string
    totalBooks: number
    totalBatches: number
}

type ParseProgressCallback = (
    phase: string,
    processed: number,
    total: number,
    percent: number
) => void

type ParseBooksBatchCallback = (
    books: ParsedBook[]
) => Promise<void> | void
```

## Examples

### Node.js — parse a local `.inpx` file

```ts
import { readFile } from "node:fs/promises"
import { parseInpxBuffer } from "inpx-parser"

const buffer = await readFile("lib.rus.ec.inpx")
const { metadata, books } = parseInpxBuffer(buffer.buffer)

console.log(metadata.description) // "Flibusta collection"
console.log(`Total books: ${metadata.totalBooks}`)

// print first 5 books
books.slice(0, 5).forEach((b) => {
    console.log(`[${b.lang}] ${b.authors} — ${b.title} (${b.ext})`)
})
```

### Node.js — with progress reporting

```ts
import { readFile } from "node:fs/promises"
import { parseInpxBuffer } from "inpx-parser"

const buffer = await readFile("lib.rus.ec.inpx")

const { books } = parseInpxBuffer(buffer.buffer, (phase, processed, total, percent) => {
    process.stdout.write(`\r${phase}: ${percent}%`)
})

console.log(`\nParsed ${books.length} books`)
```

### Node.js — streaming parse (batch processing)

```ts
import { readFile } from "node:fs/promises"
import { parseInpxBufferStreaming, type ParsedBook } from "inpx-parser"

const file = await readFile("lib.rus.ec.inpx")
const buffer = file.buffer.slice(file.byteOffset, file.byteOffset + file.byteLength)

let saved = 0
const result = await parseInpxBufferStreaming(
    buffer,
    async (booksBatch: ParsedBook[]) => {
        // Example: write this batch to DB / search index
        saved += booksBatch.length
    },
    (phase, processed, total, percent) => {
        process.stdout.write(`\r${phase}: ${processed}/${total} (${percent}%)`)
    },
    5000,
)

console.log(`\nSaved books: ${saved}`)
console.log(`Batches: ${result.totalBatches}`)
console.log(`Signature: ${result.datasetSignature}`)
```

### Browser — streaming parse with incremental UI updates

```ts
import { parseInpxBufferStreaming } from "inpx-parser"

const buffer = await fetch("/api/library/inpx").then((r) => r.arrayBuffer())

let totalRendered = 0
const result = await parseInpxBufferStreaming(
    buffer,
    (batch) => {
        totalRendered += batch.length
        // Example: append rows into virtualized list / IDB write queue
        console.log("batch size:", batch.length)
    },
    (phase, processed, total, percent) => {
        console.log(`${phase}: ${processed}/${total} (${percent}%)`)
    },
)

console.log("Total books:", result.totalBooks)
console.log("Total rendered:", totalRendered)
```

### Web Worker — streaming to IndexedDB in chunks

```ts
// worker.ts
import { parseInpxBufferStreaming } from "inpx-parser"

async function writeBooksBatchToIndexedDb(books: unknown[]): Promise<void> {
    // put books into IndexedDB object store inside a single transaction
}

self.onmessage = async (e: MessageEvent<ArrayBuffer>) => {
    const result = await parseInpxBufferStreaming(
        e.data,
        async (booksBatch) => {
            await writeBooksBatchToIndexedDb(booksBatch)
            self.postMessage({ type: "batch-written", size: booksBatch.length })
        },
        (phase, processed, total, percent) => {
            self.postMessage({ type: "progress", phase, processed, total, percent })
        },
        4000,
    )

    self.postMessage({ type: "done", result })
}
```

### Browser — fetch and parse

```ts
import { parseInpxBuffer } from "inpx-parser"

const response = await fetch("/api/library/inpx")
const buffer = await response.arrayBuffer()

const { metadata, books, datasetSignature } = parseInpxBuffer(buffer, (phase, processed, total, percent) => {
    console.log(`${phase}: ${processed}/${total} (${percent}%)`)
})

console.log(`Signature: ${datasetSignature}`)
console.log(`Books: ${metadata.totalBooks}`)
```

### Browser — inside a Web Worker

```ts
// worker.ts
import { parseInpxBuffer } from "inpx-parser"

self.onmessage = async (e: MessageEvent<ArrayBuffer>) => {
    const result = parseInpxBuffer(e.data, (phase, processed, total, percent) => {
        self.postMessage({ type: "progress", phase, percent })
    })
    self.postMessage({ type: "done", result })
}
```

### Filter books by language and extension

```ts
import { readFile } from "node:fs/promises"
import { parseInpxBuffer, type ParsedBook } from "inpx-parser"

const buffer = await readFile("lib.rus.ec.inpx")
const { books } = parseInpxBuffer(buffer.buffer)

const russianFb2 = books.filter(
    (b): b is ParsedBook => b.lang === "ru" && b.ext === "fb2"
)

console.log(`Russian FB2 books: ${russianFb2.length}`)
```

### Cache invalidation with `datasetSignature`

```ts
import { parseInpxBuffer } from "inpx-parser"

const buffer = await fetch("/api/library/inpx").then((r) => r.arrayBuffer())
const { books, datasetSignature } = parseInpxBuffer(buffer)

const cached = localStorage.getItem("sig")
if (cached === datasetSignature) {
    console.log("Library unchanged, using cache")
} else {
    localStorage.setItem("sig", datasetSignature)
    // rebuild search index, etc.
}
```

## Notes

- **Encoding**: automatically detects UTF-8 vs Windows-1251 (CP1251). Older INPX dumps from Flibusta may use CP1251; the parser handles both transparently.
- **`TextDecoder("windows-1251")`** is available in all modern browsers and in Node.js with full ICU (the default since Node 13). If unavailable, the parser falls back to UTF-8 gracefully.
- `parseInpxBuffer` returns a full `books[]` array in memory.
- `parseInpxBufferStreaming` still reads/decompresses the INPX archive in memory, but lets you process books in batches to avoid keeping one huge books array on the caller side.
- For very large INPX files (~35 MB), running parsing inside a Web Worker is strongly recommended to avoid blocking the main thread.

## License

MIT
