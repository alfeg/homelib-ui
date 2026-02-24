import { defineComponent } from "https://unpkg.com/vue@3/dist/vue.esm-browser.prod.js";

export const BooksTable = defineComponent({
    name: "BooksTable",
    props: {
        books: { type: Array, required: true },
        downloadingById: { type: Object, required: true }
    },
    emits: ["download"],
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
        </section>
    `
});