<script setup lang="ts">
defineProps<{ genres: Array<{ genre: string; label: string; count: number }>; selectedGenres: string[] }>();
defineEmits<{ (e: "toggle", genre: string): void; (e: "clear"): void }>();
</script>

<template>
  <aside class="bg-white border border-slate-200 rounded-xl p-3 lg:sticky lg:top-4 lg:max-h-[calc(100vh-2rem)] lg:overflow-auto">
    <header class="flex justify-between items-center gap-2 mb-2">
      <h2 class="m-0 text-base font-semibold">Жанры</h2>
      <button class="px-2 py-1 rounded border border-blue-900 text-blue-900 disabled:opacity-60" :disabled="!selectedGenres.length" @click="$emit('clear')">Сбросить</button>
    </header>

    <p class="text-slate-500 mb-2" v-if="genres.length">Всего жанров: {{ genres.length }}</p>
    <p class="text-slate-500 mb-2" v-else>Жанры не найдены.</p>

    <ul v-if="genres.length" class="grid gap-1">
      <li v-for="item in genres" :key="item.genre">
        <label class="grid grid-cols-[auto,1fr,auto] items-center gap-2 text-sm cursor-pointer">
          <input type="checkbox" :checked="selectedGenres.includes(item.genre)" @change="$emit('toggle', item.genre)" />
          <span class="truncate">{{ item.label || item.genre }}</span>
          <span class="text-slate-500 tabular-nums">{{ item.count }}</span>
        </label>
      </li>
    </ul>
  </aside>
</template>
