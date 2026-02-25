<script setup lang="ts">
import { useI18nState } from "../services/i18n"
import TablePagination from "./TablePagination.vue"

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
    <section>
        <div class="overflow-x-auto">
            <table class="bg-base-100 table overflow-hidden rounded-xl">
                <thead>
                    <tr class="bg-base-300/70 text-base-content">
                        <th>{{ t("table.title") }}</th>
                        <th>{{ t("table.authors") }}</th>
                        <th>{{ t("table.series") }}</th>
                        <th>{{ t("table.genres") }}</th>
                        <th>{{ t("table.lang") }}</th>
                        <th>{{ t("table.date") }}</th>
                        <th>{{ t("table.action") }}</th>
                    </tr>
                </thead>
                <tbody>
                    <tr
                        v-for="book in books"
                        :key="`${book.id}-${book.file || ''}-${book.archiveFile || ''}`"
                        class="odd:bg-base-100 even:bg-base-300/30"
                        :class="{ 'opacity-70': downloadingById[String(book.id)] }"
                    >
                        <td>{{ book.title }}</td>
                        <td>{{ book.authors }}</td>
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
