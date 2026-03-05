<script setup lang="ts">
import { ref } from "vue"

import { useI18nState } from "../../services/i18n"
import GenreList from "../molecules/GenreList.vue"
import YearRangeFilter from "../molecules/YearRangeFilter.vue"

const { t } = useI18nState()
const isCollapsed = ref(true)

defineProps<{
    genres: Array<{ genre: string; label: string; count: number }>
    selectedGenres: string[]
    yearFrom: number | null
    yearTo: number | null
    availableYearRange: { min: number; max: number } | null
}>()

defineEmits<{
    (e: "toggle", genre: string): void
    (e: "clear"): void
    (e: "yearFrom", value: number | null): void
    (e: "yearTo", value: number | null): void
    (e: "clearYear"): void
}>()

function toggleCollapse() {
    isCollapsed.value = !isCollapsed.value
}
</script>

<template>
    <aside
        class="card bg-base-100 border-base-300 flex flex-col border p-3 lg:sticky lg:top-4 lg:max-h-[calc(100vh-12rem)]"
        data-test="genre-sidebar-root"
    >
        <!-- Mobile toggle button -->
        <button
            class="btn btn-ghost btn-sm mb-2 justify-between lg:hidden"
            data-test="genre-sidebar-toggle-btn"
            @click="toggleCollapse"
        >
            <span class="font-semibold">{{ t("genres.title") }}</span>
            <svg
                class="h-5 w-5 transition-transform"
                :class="{ 'rotate-180': !isCollapsed }"
                fill="none"
                stroke="currentColor"
                stroke-width="2"
                viewBox="0 0 24 24"
                xmlns="http://www.w3.org/2000/svg"
            >
                <path
                    d="M19 9l-7 7-7-7"
                    stroke-linecap="round"
                    stroke-linejoin="round"
                />
            </svg>
        </button>

        <!-- Collapsible content -->
        <div
            :class="{ hidden: isCollapsed, 'lg:block': true }"
            data-test="genre-sidebar-content"
        >
            <GenreList
                data-test="genre-list"
                :genres="genres"
                :selectedGenres="selectedGenres"
                @clear="$emit('clear')"
                @toggle="(g) => $emit('toggle', g)"
            />

            <div
                v-if="availableYearRange"
                class="border-base-300 mt-3 shrink-0 border-t pt-3"
                data-test="year-filter-section"
            >
                <YearRangeFilter
                    :availableRange="availableYearRange"
                    data-test="year-range-filter"
                    :yearFrom="yearFrom"
                    :yearTo="yearTo"
                    @clear="$emit('clearYear')"
                    @yearFrom="(v) => $emit('yearFrom', v)"
                    @yearTo="(v) => $emit('yearTo', v)"
                />
            </div>
        </div>
    </aside>
</template>
