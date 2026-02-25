<script setup lang="ts">
import { useLocalStorage } from "@vueuse/core";
import { onMounted, watch } from "vue";
import { useLibraryState } from "./composables/useLibraryState";
import MagnetGate from "./components/MagnetGate.vue";
import LibraryControls from "./components/LibraryControls.vue";
import SearchBar from "./components/SearchBar.vue";
import GenreSidebar from "./components/GenreSidebar.vue";
import BooksTable from "./components/BooksTable.vue";

const state = useLibraryState();
const THEME_STORAGE_KEY = "mhl-ui-theme";
const selectedTheme = useLocalStorage<string>(THEME_STORAGE_KEY, "light");
const {
  isMagnetSet,
  isLoading,
  error,
  submitMagnet,
  submitTorrentFile,
  magnetHash,
  metadata,
  status,
  indexProgress,
  hasCache,
  lastUpdatedAt,
  isReindexing,
  reindexCurrent,
  resetAll,
  genreFacets,
  selectedGenres,
  toggleGenreFilter,
  clearGenreFilters,
  searchTerm,
  books,
  filteredBooks,
  pagedBooks,
  downloadingById,
  currentPage,
  totalPages,
  visibleRange,
  downloadBook,
  nextPage,
  previousPage,
  formatBookGenres,
  bootstrap
} = state;

function applyTheme(theme: string) {
  document.documentElement.setAttribute("data-theme", theme);
}

function onThemeChange(theme: string) {
  selectedTheme.value = theme;
}

function onSearchTermChange(value: string) {
  searchTerm.value = value;
}

watch(selectedTheme, (theme) => {
  applyTheme(theme);
}, { immediate: true });

onMounted(() => {
  selectedTheme.value = selectedTheme.value === "dark" ? "dark" : "light";
  applyTheme(selectedTheme.value);
  bootstrap();
});
</script>

<template>
  <main class="min-h-screen bg-base-200 text-base-content">
    <MagnetGate
      v-if="!isMagnetSet"
      :loading="isLoading"
      :error="error"
      @submit="submitMagnet"
      @submit-torrent="submitTorrentFile"
    />

    <section v-else class="max-w-[1800px] mx-auto p-5">
      <LibraryControls
        :hash="magnetHash"
        :metadata="metadata"
        :status="status"
        :progress="indexProgress"
        :has-cache="hasCache"
        :last-updated-at="lastUpdatedAt"
        :reindexing="isReindexing"
        :theme="selectedTheme"
        @reindex="reindexCurrent"
        @reset="resetAll"
        @theme-toggle="(enabled) => onThemeChange(enabled ? 'dark' : 'light')"
      />

      <div class="grid grid-cols-1 lg:grid-cols-[420px_1fr] gap-6 items-start">
        <GenreSidebar
          :genres="genreFacets"
          :selected-genres="selectedGenres"
          @toggle="toggleGenreFilter"
          @clear="clearGenreFilters"
        />

        <div class="min-w-0 card bg-base-100 border border-base-300 shadow-sm p-4">
          <SearchBar
            :model-value="searchTerm"
            @update:model-value="onSearchTermChange"
            :total="books.length"
            :filtered="filteredBooks.length"
          />

          <p v-if="error" class="alert alert-error mb-2">{{ error }}</p>

          <BooksTable
            :books="pagedBooks"
            :downloading-by-id="downloadingById"
            :current-page="currentPage"
            :total-pages="totalPages"
            :visible-range="visibleRange"
            :total-results="filteredBooks.length"
            :format-genres="formatBookGenres"
            @download="downloadBook"
            @next-page="nextPage"
            @previous-page="previousPage"
          />
        </div>
      </div>
    </section>
  </main>
</template>
