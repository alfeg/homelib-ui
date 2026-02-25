<script setup lang="ts">
import { useI18nState } from "../services/i18n"

defineProps<{
    currentPage: number
    totalPages: number
    visibleRange: { start: number; end: number }
    totalResults: number
}>()

defineEmits<{
    (e: "next-page"): void
    (e: "previous-page"): void
}>()

const { t } = useI18nState()
</script>

<template>
    <footer
        v-if="totalResults"
        class="mt-3 flex flex-wrap items-center justify-between gap-2"
    >
        <p class="text-base-content/70">
            {{ t("table.range", { start: visibleRange.start, end: visibleRange.end, total: totalResults }) }}
        </p>
        <div class="inline-flex items-center gap-2">
            <button
                class="btn btn-outline btn-primary btn-sm"
                :disabled="currentPage <= 1"
                @click="$emit('previous-page')"
            >
                {{ t("buttons.previous") }}
            </button>
            <span class="text-base-content/70">{{ t("table.page", { page: currentPage, totalPages }) }}</span>
            <button
                class="btn btn-outline btn-primary btn-sm"
                :disabled="currentPage >= totalPages"
                @click="$emit('next-page')"
            >
                {{ t("buttons.next") }}
            </button>
        </div>
    </footer>
</template>
