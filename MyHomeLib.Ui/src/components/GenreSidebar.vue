<script setup lang="ts">
import { useI18nState } from "../services/i18n"

defineProps<{ genres: Array<{ genre: string; label: string; count: number }>; selectedGenres: string[] }>()
defineEmits<{ (e: "toggle", genre: string): void; (e: "clear"): void }>()
const { t } = useI18nState()
</script>

<template>
    <aside
        class="card bg-base-100 border-base-300 border p-3 lg:sticky lg:top-4 lg:max-h-[calc(100vh-2rem)] lg:overflow-auto"
    >
        <header class="mb-2 flex items-center justify-between gap-2">
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
            class="text-base-content/70 mb-2"
        >
            {{ t("genres.total", { count: genres.length }) }}
        </p>
        <p
            v-else
            class="text-base-content/70 mb-2"
        >
            {{ t("genres.notFound") }}
        </p>

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
    </aside>
</template>
