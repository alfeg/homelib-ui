<script setup lang="ts">
import { useI18nState } from "../services/i18n";

defineProps<{ books: any[]; downloadingById: Record<string, boolean>; currentPage: number; totalPages: number; visibleRange: { start: number; end: number }; totalResults: number; formatGenres?: (book: any) => string }>();
defineEmits<{ (e: "download", book: any): void; (e: "next-page"): void; (e: "previous-page"): void }>();
const { t } = useI18nState();
</script>

<template>
  <section>
    <div class="overflow-x-auto">
      <table class="table bg-base-100 rounded-xl overflow-hidden">
        <thead>
          <tr class="bg-base-300/70 text-base-content">
            <th>{{ t("table.title") }}</th>
            <th>{{ t("table.authors") }}</th>
            <th>{{ t("table.series") }}</th>
            <th>{{ t("table.genres") }}</th>
            <th>{{ t("table.lang") }}</th>
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
              <span v-if="book.series">{{ book.series }}<span v-if="book.seriesNo"> #{{ book.seriesNo }}</span></span>
              <span v-else>—</span>
            </td>
            <td class="max-w-[240px] truncate" :title="formatGenres ? formatGenres(book) : (book.genre || '—')">{{ formatGenres ? formatGenres(book) : (book.genre || "—") }}</td>
            <td>{{ book.lang || '—' }}</td>
            <td>
              <button class="btn btn-outline btn-primary btn-xs" :disabled="downloadingById[String(book.id)]" @click="$emit('download', book)">
                {{ downloadingById[String(book.id)] ? t("buttons.downloading") : t("buttons.download") }}
              </button>
            </td>
          </tr>
          <tr v-if="!books.length">
            <td colspan="6" class="text-center text-base-content/70 p-3">{{ t("table.noBooks") }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <footer v-if="totalResults" class="mt-3 flex justify-between items-center gap-2 flex-wrap">
      <p class="text-base-content/70">{{ t("table.range", { start: visibleRange.start, end: visibleRange.end, total: totalResults }) }}</p>
      <div class="inline-flex gap-2 items-center">
        <button class="btn btn-outline btn-primary btn-sm" :disabled="currentPage <= 1" @click="$emit('previous-page')">{{ t("buttons.previous") }}</button>
        <span class="text-base-content/70">{{ t("table.page", { page: currentPage, totalPages }) }}</span>
        <button class="btn btn-outline btn-primary btn-sm" :disabled="currentPage >= totalPages" @click="$emit('next-page')">{{ t("buttons.next") }}</button>
      </div>
    </footer>
  </section>
</template>
