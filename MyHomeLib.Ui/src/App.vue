<script setup lang="ts">
import { useLocalStorage } from "@vueuse/core"

import { onMounted, watch } from "vue"

import BooksTable from "./components/BooksTable.vue"
import GenreSidebar from "./components/GenreSidebar.vue"
import LibraryControls from "./components/LibraryControls.vue"
import MagnetGate from "./components/MagnetGate.vue"
import SearchBar from "./components/SearchBar.vue"
import { useLibraryState } from "./composables/useLibraryState"

const state = useLibraryState()
const THEME_STORAGE_KEY = "mhl-ui-theme"
const selectedTheme = useLocalStorage<string>(THEME_STORAGE_KEY, "light")
const {
    isMagnetSet,
    isReady,
    isLoading,
    error,
    submitMagnet,
    submitTorrentFile,
    magnetHash,
    metadata,
    status,
    indexProgress,
    progressLabel,
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
    totalBooks,
    filteredBooks,
    totalFilteredBooks,
    downloadingById,
    currentPage,
    totalPages,
    visibleRange,
    downloadBook,
    nextPage,
    previousPage,
    formatBookGenres,
    bootstrap,
} = state

function applyTheme(theme: string) {
    document.documentElement.setAttribute("data-theme", theme)
}

function onThemeChange(theme: string) {
    selectedTheme.value = theme
}

watch(
    selectedTheme,
    (theme) => {
        applyTheme(theme)
    },
    { immediate: true },
)

onMounted(() => {
    selectedTheme.value = selectedTheme.value === "dark" ? "dark" : "light"
    applyTheme(selectedTheme.value)
    bootstrap()
})
</script>

<template>
    <main class="bg-base-200 text-base-content min-h-screen">
        <MagnetGate
            v-if="!isMagnetSet || (!isReady && !isReindexing)"
            :error="error"
            :loading="isLoading"
            :progress="isMagnetSet ? indexProgress : undefined"
            :progressLabel="progressLabel"
            :statusText="status"
            @dismiss="resetAll"
            @submit="submitMagnet"
            @submitTorrent="submitTorrentFile"
        />

        <section
            v-else
            class="mx-auto max-w-[1800px] p-5"
        >
            <LibraryControls
                :hasCache="hasCache"
                :hash="magnetHash"
                :lastUpdatedAt="lastUpdatedAt"
                :metadata="metadata"
                :progress="indexProgress"
                :reindexing="isReindexing"
                :status="status"
                :theme="selectedTheme"
                @reindex="reindexCurrent"
                @reset="resetAll"
                @themeToggle="(enabled) => onThemeChange(enabled ? 'dark' : 'light')"
            />

            <div class="grid grid-cols-1 items-start gap-6 lg:grid-cols-[420px_1fr]">
                <GenreSidebar
                    :genres="genreFacets"
                    :selectedGenres="selectedGenres"
                    @clear="clearGenreFilters"
                    @toggle="toggleGenreFilter"
                />

                <div class="card bg-base-100 border-base-300 min-w-0 border p-4 shadow-sm">
                    <SearchBar
                        v-model="searchTerm"
                        :filtered="totalFilteredBooks"
                        :total="totalBooks"
                    />

                    <p
                        v-if="error"
                        class="alert alert-error mb-2"
                    >
                        {{ error }}
                    </p>

                    <BooksTable
                        :books="filteredBooks"
                        :currentPage="currentPage"
                        :downloadingById="downloadingById"
                        :formatGenres="formatBookGenres"
                        :totalPages="totalPages"
                        :totalResults="totalFilteredBooks"
                        :visibleRange="visibleRange"
                        @download="downloadBook"
                        @nextPage="nextPage"
                        @previousPage="previousPage"
                    />
                </div>
            </div>
        </section>
    </main>
</template>
