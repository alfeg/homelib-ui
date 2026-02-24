import { Index as FlexIndex } from "https://cdn.jsdelivr.net/npm/flexsearch@0.8.212/dist/flexsearch.bundle.module.min.js";

const INDEX_CHUNK_SIZE = 250;

let index = null;
let booksById = new Map();

function toSearchText(book) {
    return [book.title, book.authors, book.series, book.lang, book.file]
        .filter(Boolean)
        .join(" ");
}

self.onmessage = async (event) => {
    const message = event?.data ?? {};

    if (message.type === "build") {
        const books = Array.isArray(message.books) ? message.books : [];
        const total = books.length;

        index = new FlexIndex({ tokenize: "forward", cache: true });
        booksById = new Map();

        if (!total) {
            self.postMessage({
                type: "build-progress",
                payload: {
                    phase: "indexing",
                    processed: 0,
                    total: 0,
                    percent: 100
                }
            });

            self.postMessage({
                type: "build-complete",
                payload: {
                    total: 0
                }
            });
            return;
        }

        for (let i = 0; i < total; i += 1) {
            const book = books[i];
            const id = String(book.id);
            booksById.set(id, book);
            index.add(id, toSearchText(book));

            const processed = i + 1;
            if (processed % INDEX_CHUNK_SIZE === 0 || processed === total) {
                self.postMessage({
                    type: "build-progress",
                    payload: {
                        phase: "indexing",
                        processed,
                        total,
                        percent: Math.round((processed / total) * 100)
                    }
                });

                await new Promise((resolve) => setTimeout(resolve, 0));
            }
        }

        self.postMessage({
            type: "build-complete",
            payload: {
                total
            }
        });

        return;
    }

    if (message.type === "search") {
        const requestId = message.requestId;
        const term = typeof message.term === "string" ? message.term.trim() : "";
        const limit = Number.isFinite(message.limit) ? message.limit : 1000;

        if (!term || !index) {
            self.postMessage({
                type: "search-result",
                payload: {
                    requestId,
                    books: []
                }
            });
            return;
        }

        const books = index.search(term, { limit })
            .map((id) => booksById.get(String(id)))
            .filter(Boolean);

        self.postMessage({
            type: "search-result",
            payload: {
                requestId,
                books
            }
        });
    }
};