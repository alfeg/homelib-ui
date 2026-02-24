import { defineComponent } from "https://unpkg.com/vue@3/dist/vue.esm-browser.prod.js";

export const BooksTable = defineComponent({
    name: "BooksTable",
    props: {
        books: { type: Array, required: true },
        downloadingById: { type: Object, required: true },
        currentPage: { type: Number, required: true },
        totalPages: { type: Number, required: true },
        visibleRange: { type: Object, required: true },
        totalResults: { type: Number, required: true }
    },
    emits: ["download", "go-to-page", "next-page", "previous-page"],
    methods: {
        isDownloading(bookId) {
            return !!this.downloadingById[bookId];
        }
    },
    template: `
        <section>
            <table class="books-table">
                <thead>
                    <tr>
                        <th>Title</th>
                        <th>Authors</th>
                        <th>Series</th>
                        <th>Lang</th>
                        <th>Action</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="book in books" :key="book.id" :class="{ loading: isDownloading(book.id) }">
                        <td>{{ book.title }}</td>
                        <td>{{ book.authors }}</td>
                        <td>
                            <span v-if="book.series">{{ book.series }}<span v-if="book.seriesNo"> #{{ book.seriesNo }}</span></span>
                            <span v-else>—</span>
                        </td>
                        <td>{{ book.lang || '—' }}</td>
                        <td>
                            <button
                                class="btn"
                                :disabled="isDownloading(book.id)"
                                @click="$emit('download', book)">
                                {{ isDownloading(book.id) ? 'Downloading...' : 'Download' }}
                            </button>
                        </td>
                    </tr>
                    <tr v-if="!books.length">
                        <td colspan="5" class="empty-cell">No books found.</td>
                    </tr>
                </tbody>
            </table>

            <footer v-if="totalResults" class="table-pagination">
                <p class="subtle pagination-info">
                    Showing {{ visibleRange.start }}-{{ visibleRange.end }} of {{ totalResults }} books
                </p>
                <div class="pagination-actions">
                    <button
                        class="btn"
                        :disabled="currentPage <= 1"
                        @click="$emit('previous-page')">
                        Previous
                    </button>
                    <span class="subtle">Page {{ currentPage }} / {{ totalPages }}</span>
                    <button
                        class="btn"
                        :disabled="currentPage >= totalPages"
                        @click="$emit('next-page')">
                        Next
                    </button>
                </div>
            </footer>
        </section>
    `
});