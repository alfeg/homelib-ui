<script setup lang="ts">
import GenreList from "./GenreList.vue"
import YearRangeFilter from "./YearRangeFilter.vue"

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
</script>

<template>
    <aside
        class="card bg-base-100 border-base-300 flex flex-col border p-3 lg:sticky lg:top-4 lg:max-h-[calc(100vh-12rem)]"
    >
        <GenreList
            :genres="genres"
            :selectedGenres="selectedGenres"
            @clear="$emit('clear')"
            @toggle="(g) => $emit('toggle', g)"
        />

        <div
            v-if="availableYearRange"
            class="border-base-300 mt-3 shrink-0 border-t pt-3"
        >
            <YearRangeFilter
                :availableRange="availableYearRange"
                :yearFrom="yearFrom"
                :yearTo="yearTo"
                @clear="$emit('clearYear')"
                @yearFrom="(v) => $emit('yearFrom', v)"
                @yearTo="(v) => $emit('yearTo', v)"
            />
        </div>
    </aside>
</template>
