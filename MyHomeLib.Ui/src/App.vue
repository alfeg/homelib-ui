<script setup lang="ts">
import { onMounted, unref } from "vue";
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
      :loading="unref(state.isLoading)"
      :error="unref(state.error)"
      @submit="state.submitMagnet"
      @submit-torrent="state.submitTorrentFile"
    />

    <section v-else class="max-w-7xl mx-auto p-5">
      <LibraryControls
        :hash="unref(state.magnetHash)"
        :metadata="unref(state.metadata)"
        :status="unref(state.status)"
        :progress="unref(state.indexProgress)"
        :has-cache="unref(state.hasCache)"
        :last-updated-at="unref(state.lastUpdatedAt)"
        :reindexing="unref(state.isReindexing)"
        @reindex="state.reindexCurrent"
        @reset="state.resetAll"
      />

      <div class="grid grid-cols-1 lg:grid-cols-[260px,minmax(0,1fr)] gap-4 items-start">
        <GenreSidebar
          :genres="unref(state.genreFacets)"
          :selected-genres="unref(state.selectedGenres)"
          @toggle="state.toggleGenreFilter"
          @clear="state.clearGenreFilters"
        />

        <div class="min-w-0">
          <SearchBar
            :model-value="unref(state.searchTerm)"
            @update:model-value="state.searchTerm = $event"
            :total="unref(state.books).length"
            :filtered="unref(state.filteredBooks).length"
          />

          <p v-if="state.error" class="text-red-700 mb-2">{{ state.error }}</p>

          <BooksTable
            :books="unref(state.pagedBooks)"
            :downloading-by-id="unref(state.downloadingById)"
            :current-page="unref(state.currentPage)"
            :total-pages="unref(state.totalPages)"
            :visible-range="unref(state.visibleRange)"
            :total-results="unref(state.filteredBooks).length"
            @download="state.downloadBook"
            @next-page="state.nextPage"
            @previous-page="state.previousPage"
          />
        </div>
      </div>
    </section>
  </main>
</template>
