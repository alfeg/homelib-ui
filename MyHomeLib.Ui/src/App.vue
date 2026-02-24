<script setup lang="ts">
import { onMounted } from "vue";
import { useLibraryState } from "./composables/useLibraryState";
import MagnetGate from "./components/MagnetGate.vue";
import LibraryControls from "./components/LibraryControls.vue";
import SearchBar from "./components/SearchBar.vue";
import GenreSidebar from "./components/GenreSidebar.vue";
import BooksTable from "./components/BooksTable.vue";

const state = useLibraryState();
onMounted(() => state.bootstrap());
</script>

<template>
  <main>
    <MagnetGate
      v-if="!state.isMagnetSet"
      :loading="state.isLoading"
      :error="state.error"
      @submit="state.submitMagnet"
      @submit-torrent="state.submitTorrentFile"
    />

    <section v-else class="max-w-7xl mx-auto p-5">
      <LibraryControls
        :hash="state.magnetHash"
        :metadata="state.metadata"
        :status="state.status"
        :progress="state.indexProgress"
        :has-cache="state.hasCache"
        :last-updated-at="state.lastUpdatedAt"
        :reindexing="state.isReindexing"
        @reindex="state.reindexCurrent"
        @reset="state.resetAll"
      />

      <div class="grid grid-cols-1 lg:grid-cols-[260px,minmax(0,1fr)] gap-4 items-start">
        <GenreSidebar
          :genres="state.genreFacets"
          :selected-genres="state.selectedGenres"
          @toggle="state.toggleGenreFilter"
          @clear="state.clearGenreFilters"
        />

        <div class="min-w-0">
          <SearchBar
            v-model="state.searchTerm"
            :total="state.books.length"
            :filtered="state.filteredBooks.length"
          />

          <p v-if="state.error" class="text-red-700 mb-2">{{ state.error }}</p>

          <BooksTable
            :books="state.pagedBooks"
            :downloading-by-id="state.downloadingById"
            :current-page="state.currentPage"
            :total-pages="state.totalPages"
            :visible-range="state.visibleRange"
            :total-results="state.filteredBooks.length"
            @download="state.downloadBook"
            @next-page="state.nextPage"
            @previous-page="state.previousPage"
          />
        </div>
      </div>
    </section>
  </main>
</template>
