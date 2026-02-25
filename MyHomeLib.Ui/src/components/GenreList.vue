<script setup lang="ts">
import { useI18nState } from "../services/i18n"

defineProps<{
    genres: Array<{ genre: string; label: string; count: number }>
    selectedGenres: string[]
}>()

defineEmits<{
    (e: "toggle", genre: string): void
    (e: "clear"): void
}>()

const { t } = useI18nState()
</script>

<template>
    <div class="flex min-h-0 flex-1 flex-col">
        <header class="mb-2 flex shrink-0 items-center justify-between gap-2">
            <h2 class="m-0 text-base font-semibold">{{ t("genres.title") }}</h2>
            <button
                class="btn btn-outline btn-primary btn-xs"
                :disabled="!selectedGenres.length"
                @click="$emit('clear')"
            >
                {{ t("genres.reset") }}
            </button>
        </header>

        <p class="text-base-content/70 mb-2 shrink-0 text-sm">
            {{ genres.length ? t("genres.total", { count: genres.length }) : t("genres.notFound") }}
        </p>

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
    </div>
</template>
