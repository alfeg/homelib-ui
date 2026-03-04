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
    magnetUri,
    metadata,
    indexProgress,
    progressLabel,
    hasCache,
    lastUpdatedAt,
    isReindexing,
    openLibraryGate,
    resetAll,
    genreFacets,
    selectedGenres,
    toggleGenreFilter,
    clearGenreFilters,
    selectedYearFrom,
    selectedYearTo,
    availableYearRange,
    clearYearFilter,
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
    <main class="bg-base-200 text-base-content min-h-screen transition-all duration-500">
        <MagnetGate
            v-if="!isMagnetSet || (!isReady && !isReindexing && indexProgress.phase !== 'loading-cache')"
            :error="error"
            :loading="isLoading"
            :progress="isMagnetSet ? indexProgress : undefined"
            :progressLabel="progressLabel"
            @dismiss="resetAll"
            @submit="submitMagnet"
            @submitTorrent="submitTorrentFile"
        />

        <section
            v-else
            class="mx-auto max-w-450 p-5"
        >
            <LibraryControls
                :hasCache="hasCache"
                :hash="magnetHash"
                :lastUpdatedAt="lastUpdatedAt"
                :magnetUri="magnetUri"
                :metadata="metadata"
                :progress="indexProgress"
                :reindexing="isReindexing"
                :theme="selectedTheme"
                @changeLibrary="openLibraryGate"
                @reset="resetAll"
                @themeToggle="(enabled) => onThemeChange(enabled ? 'dark' : 'light')"
            />

            <div
                v-if="isReady"
                class="grid grid-cols-1 items-start gap-6 lg:grid-cols-[420px_1fr]"
            >
                <GenreSidebar
                    :availableYearRange="availableYearRange"
                    :genres="genreFacets"
                    :selectedGenres="selectedGenres"
                    :yearFrom="selectedYearFrom"
                    :yearTo="selectedYearTo"
                    @clear="clearGenreFilters"
                    @clearYear="clearYearFilter"
                    @toggle="toggleGenreFilter"
                    @yearFrom="(v) => (selectedYearFrom = v)"
                    @yearTo="(v) => (selectedYearTo = v)"
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

            <div
                v-else
                class="grid grid-cols-1 items-start gap-6 lg:grid-cols-[420px_1fr]"
            >
                <aside class="card bg-base-100 border-base-300 flex flex-col border p-3">
                    <div class="skeleton mb-3 h-6 w-32" />
                    <div class="skeleton mb-2 h-4 w-full" />
                    <div class="skeleton mb-2 h-4 w-full" />
                    <div class="skeleton mb-2 h-4 w-5/6" />
                    <div class="border-base-300 mt-3 border-t pt-3">
                        <div class="skeleton mb-2 h-4 w-20" />
                        <div class="skeleton mb-2 h-9 w-full" />
                        <div class="skeleton h-9 w-full" />
                    </div>
                </aside>

                <div class="card bg-base-100 border-base-300 min-w-0 border p-4 shadow-sm">
                    <div class="skeleton mb-4 h-10 w-full" />
                    <div class="skeleton mb-3 h-10 w-full" />
                    <div class="skeleton mb-2 h-12 w-full" />
                    <div class="skeleton mb-2 h-12 w-full" />
                    <div class="skeleton h-12 w-full" />
                </div>
            </div>
        </section>
    </main>
</template>
