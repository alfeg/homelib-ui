<script setup lang="ts">
import { useI18nState } from "../services/i18n"

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
const { t } = useI18nState()

function parseYear(value: string): number | null {
    const n = parseInt(value, 10)
    return Number.isFinite(n) && n > 1000 ? n : null
}
</script>

<template>
    <aside
        class="card bg-base-100 border-base-300 flex flex-col border p-3 lg:sticky lg:top-4 lg:max-h-[calc(100vh-2rem)]"
    >
        <!-- Genres header -->
        <header class="mb-2 shrink-0 flex items-center justify-between gap-2">
            <h2 class="m-0 text-base font-semibold">{{ t("genres.title") }}</h2>
            <button
                class="btn btn-outline btn-primary btn-xs"
                :disabled="!selectedGenres.length"
                @click="$emit('clear')"
            >
                {{ t("genres.reset") }}
            </button>
        </header>

        <p
            v-if="genres.length"
            class="text-base-content/70 mb-2 shrink-0 text-sm"
        >
            {{ t("genres.total", { count: genres.length }) }}
        </p>
        <p
            v-else
            class="text-base-content/70 mb-2 shrink-0 text-sm"
        >
            {{ t("genres.notFound") }}
        </p>

        <!-- Scrollable genre list -->
        <div class="min-h-0 flex-1 overflow-y-auto">
            <ul
                v-if="genres.length"
                class="m-0 grid list-none gap-1 p-0"
            >
                <li
                    v-for="item in genres"
                    :key="item.genre"
                >
                    <div
                        class="grid cursor-pointer grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-2 text-sm"
                        @click="$emit('toggle', item.genre)"
                    >
                        <input
                            :checked="selectedGenres.includes(item.genre)"
                            class="checkbox checkbox-sm"
                            type="checkbox"
                            @click.stop="$emit('toggle', item.genre)"
                        />
                        <span
                            class="truncate"
                            :title="item.label || item.genre"
                            >{{ item.label || item.genre }}</span
                        >
                        <span class="text-base-content/70 tabular-nums">{{ item.count }}</span>
                    </div>
                </li>
            </ul>
        </div>

        <!-- Year range filter -->
        <div
            v-if="availableYearRange"
            class="border-base-300 mt-3 shrink-0 border-t pt-3"
        >
            <div class="mb-2 flex items-center justify-between gap-2">
                <h2 class="m-0 text-base font-semibold">{{ t("yearRange.title") }}</h2>
                <button
                    class="btn btn-outline btn-primary btn-xs"
                    :disabled="yearFrom === null && yearTo === null"
                    @click="$emit('clearYear')"
                >
                    {{ t("yearRange.reset") }}
                </button>
            </div>
            <div class="flex items-center gap-2">
                <label class="flex flex-1 flex-col gap-0.5">
                    <span class="text-base-content/60 text-xs">{{ t("yearRange.from") }}</span>
                    <input
                        class="input input-xs input-bordered w-full"
                        type="number"
                        :min="availableYearRange.min"
                        :max="availableYearRange.max"
                        :placeholder="String(availableYearRange.min)"
                        :value="yearFrom ?? ''"
                        @change="$emit('yearFrom', parseYear(($event.target as HTMLInputElement).value))"
                    />
                </label>
                <span class="text-base-content/40 mt-4">—</span>
                <label class="flex flex-1 flex-col gap-0.5">
                    <span class="text-base-content/60 text-xs">{{ t("yearRange.to") }}</span>
                    <input
                        class="input input-xs input-bordered w-full"
                        type="number"
                        :min="availableYearRange.min"
                        :max="availableYearRange.max"
                        :placeholder="String(availableYearRange.max)"
                        :value="yearTo ?? ''"
                        @change="$emit('yearTo', parseYear(($event.target as HTMLInputElement).value))"
                    />
                </label>
            </div>
            <p class="text-base-content/50 mt-1 text-xs">{{ availableYearRange.min }} – {{ availableYearRange.max }}</p>
        </div>
    </aside>
</template>
