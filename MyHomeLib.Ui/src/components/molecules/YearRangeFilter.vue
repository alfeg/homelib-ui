<script setup lang="ts">
import { useI18nState } from "../../services/i18n"

const props = defineProps<{
    yearFrom: number | null
    yearTo: number | null
    availableRange: { min: number; max: number }
}>()

defineEmits<{
    (e: "yearFrom", value: number | null): void
    (e: "yearTo", value: number | null): void
    (e: "clear"): void
}>()

const { t } = useI18nState()

function parseYear(value: string): number | null {
    const n = parseInt(value, 10)
    return Number.isFinite(n) && n > 1000 ? n : null
}

const hasFilter = () => props.yearFrom !== null || props.yearTo !== null
</script>

<template>
    <div data-test="year-range-root">
        <div class="mb-2 flex items-center justify-between gap-2">
            <h2
                class="m-0 text-base font-semibold"
                data-test="year-range-title"
            >
                {{ t("yearRange.title") }}
            </h2>
            <button
                class="btn btn-outline btn-primary btn-xs"
                data-test="year-range-reset-btn"
                :disabled="!hasFilter()"
                @click="$emit('clear')"
            >
                {{ t("yearRange.reset") }}
            </button>
        </div>

        <div class="flex items-center gap-2">
            <label class="flex flex-1 flex-col gap-0.5">
                <span
                    class="text-base-content/60 text-xs"
                    data-test="year-range-from-label"
                >
                    {{ t("yearRange.from") }}
                </span>
                <input
                    class="input input-xs input-bordered w-full"
                    data-test="year-range-from-input"
                    :max="availableRange.max"
                    :min="availableRange.min"
                    :placeholder="String(availableRange.min)"
                    type="number"
                    :value="yearFrom ?? ''"
                    @change="$emit('yearFrom', parseYear(($event.target as HTMLInputElement).value))"
                />
            </label>
            <span class="text-base-content/40 mt-4">—</span>
            <label class="flex flex-1 flex-col gap-0.5">
                <span
                    class="text-base-content/60 text-xs"
                    data-test="year-range-to-label"
                >
                    {{ t("yearRange.to") }}
                </span>
                <input
                    class="input input-xs input-bordered w-full"
                    data-test="year-range-to-input"
                    :max="availableRange.max"
                    :min="availableRange.min"
                    :placeholder="String(availableRange.max)"
                    type="number"
                    :value="yearTo ?? ''"
                    @change="$emit('yearTo', parseYear(($event.target as HTMLInputElement).value))"
                />
            </label>
        </div>

        <p
            class="text-base-content/50 mt-1 text-xs"
            data-test="year-range-bounds"
        >
            {{ availableRange.min }} – {{ availableRange.max }}
        </p>
    </div>
</template>
