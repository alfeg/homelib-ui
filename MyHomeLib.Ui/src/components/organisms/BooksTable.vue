<script setup lang="ts">
import { useI18nState } from "../../services/i18n"
import TablePagination from "../molecules/TablePagination.vue"

defineProps<{
    books: any[]
    downloadingById: Record<string, boolean>
    currentPage: number
    totalPages: number
    visibleRange: { start: number; end: number }
    totalResults: number
    formatGenres?: (book: any) => string
}>()
defineEmits<{ (e: "download", book: any): void; (e: "next-page"): void; (e: "previous-page"): void }>()
const { t } = useI18nState()
</script>

<template>
    <section data-test="books-table-root">
        <div
            class="overflow-x-auto"
            data-test="books-table-scroll"
        >
            <table
                class="bg-base-100 table overflow-hidden rounded-xl"
                data-test="books-table-table"
            >
                <thead>
                    <tr
                        class="bg-base-300/70 text-base-content"
                        data-test="books-table-header-row"
                    >
                        <th data-test="books-table-col-title">{{ t("table.title") }}</th>
                        <th data-test="books-table-col-authors">{{ t("table.authors") }}</th>
                        <th data-test="books-table-col-series">{{ t("table.series") }}</th>
                        <th data-test="books-table-col-genres">{{ t("table.genres") }}</th>
                        <th data-test="books-table-col-lang">{{ t("table.lang") }}</th>
                        <th data-test="books-table-col-date">{{ t("table.date") }}</th>
                        <th data-test="books-table-col-action">{{ t("table.action") }}</th>
                    </tr>
                </thead>
                <tbody>
                    <tr
                        v-for="book in books"
                        :key="`${book.id}-${book.file || ''}-${book.archiveFile || ''}`"
                        class="odd:bg-base-100 even:bg-base-300/30"
                        :class="{ 'opacity-70': downloadingById[String(book.id)] }"
                        data-test="books-table-row"
                    >
                        <td data-test="books-table-book-title">{{ book.title }}</td>
                        <td data-test="books-table-book-authors">{{ book.authors }}</td>
                        <td>
                            <span v-if="book.series"
                                >{{ book.series }}<span v-if="book.seriesNo"> #{{ book.seriesNo }}</span></span
                            >
                            <span v-else>—</span>
                        </td>
                        <td
                            class="max-w-60 truncate"
                            :title="formatGenres ? formatGenres(book) : book.genre || '—'"
                        >
                            {{ formatGenres ? formatGenres(book) : book.genre || "—" }}
                        </td>
                        <td>{{ book.lang || "—" }}</td>
                        <td class="text-base-content/60 text-xs whitespace-nowrap">{{ book.date || "—" }}</td>
                        <td>
                            <button
                                class="btn btn-primary btn-xs"
                                :class="downloadingById[String(book.id)] ? 'animate-pulse opacity-80' : 'btn-outline'"
                                data-test="books-table-download-btn"
                                :disabled="downloadingById[String(book.id)]"
                                @click="$emit('download', book)"
                            >
                                {{
                                    downloadingById[String(book.id)] ? t("buttons.downloading") : t("buttons.download")
                                }}
                            </button>
                        </td>
                    </tr>
                    <tr v-if="!books.length">
                        <td
                            class="text-base-content/70 p-3 text-center"
                            colspan="7"
                            data-test="books-table-empty"
                        >
                            {{ t("table.noBooks") }}
                        </td>
                    </tr>
                </tbody>
            </table>
        </div>

        <TablePagination
            :currentPage="currentPage"
            :totalPages="totalPages"
            :totalResults="totalResults"
            :visibleRange="visibleRange"
            @nextPage="$emit('next-page')"
            @previousPage="$emit('previous-page')"
        />
    </section>
</template>
