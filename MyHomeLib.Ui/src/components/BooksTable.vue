<script setup lang="ts">
defineProps<{ books: any[]; downloadingById: Record<string, boolean>; currentPage: number; totalPages: number; visibleRange: { start: number; end: number }; totalResults: number }>();
defineEmits<{ (e: "download", book: any): void; (e: "next-page"): void; (e: "previous-page"): void }>();
</script>

<template>
  <section>
    <div class="overflow-x-auto">
      <table class="w-full border-collapse bg-white rounded-xl overflow-hidden">
        <thead>
          <tr class="bg-slate-50">
            <th class="text-left p-2 border-b">Title</th>
            <th class="text-left p-2 border-b">Authors</th>
            <th class="text-left p-2 border-b">Series</th>
            <th class="text-left p-2 border-b">Lang</th>
            <th class="text-left p-2 border-b">Action</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="book in books" :key="book.id" :class="{ 'opacity-70': downloadingById[String(book.id)] }">
            <td class="p-2 border-b">{{ book.title }}</td>
            <td class="p-2 border-b">{{ book.authors }}</td>
            <td class="p-2 border-b">
              <span v-if="book.series">{{ book.series }}<span v-if="book.seriesNo"> #{{ book.seriesNo }}</span></span>
              <span v-else>—</span>
            </td>
            <td class="p-2 border-b">{{ book.lang || '—' }}</td>
            <td class="p-2 border-b">
              <button class="px-2 py-1 rounded border border-blue-900 text-blue-900 disabled:opacity-60" :disabled="downloadingById[String(book.id)]" @click="$emit('download', book)">
                {{ downloadingById[String(book.id)] ? 'Downloading...' : 'Download' }}
              </button>
            </td>
          </tr>
          <tr v-if="!books.length">
            <td colspan="5" class="text-center text-slate-500 p-3">No books found.</td>
          </tr>
        </tbody>
      </table>
    </div>

    <footer v-if="totalResults" class="mt-3 flex justify-between items-center gap-2 flex-wrap">
      <p class="text-slate-500">Showing {{ visibleRange.start }}-{{ visibleRange.end }} of {{ totalResults }} books</p>
      <div class="inline-flex gap-2 items-center">
        <button class="px-2 py-1 rounded border border-blue-900 text-blue-900 disabled:opacity-60" :disabled="currentPage <= 1" @click="$emit('previous-page')">Previous</button>
        <span class="text-slate-500">Page {{ currentPage }} / {{ totalPages }}</span>
        <button class="px-2 py-1 rounded border border-blue-900 text-blue-900 disabled:opacity-60" :disabled="currentPage >= totalPages" @click="$emit('next-page')">Next</button>
      </div>
    </footer>
  </section>
</template>
