<script setup lang="ts">
defineProps<{ genres: Array<{ genre: string; label: string; count: number }>; selectedGenres: string[] }>();
defineEmits<{ (e: "toggle", genre: string): void; (e: "clear"): void }>();
</script>

<template>
  <aside class="card bg-base-100 border border-base-300 p-3 lg:sticky lg:top-4 lg:max-h-[calc(100vh-2rem)] lg:overflow-auto">
    <header class="flex justify-between items-center gap-2 mb-2">
      <h2 class="m-0 text-base font-semibold">Жанры</h2>
      <button class="btn btn-outline btn-primary btn-xs" :disabled="!selectedGenres.length" @click="$emit('clear')">Сбросить</button>
    </header>

    <p class="text-base-content/70 mb-2" v-if="genres.length">Всего жанров: {{ genres.length }}</p>
    <p class="text-base-content/70 mb-2" v-else>Жанры не найдены.</p>

    <ul v-if="genres.length" class="grid gap-1 list-none m-0 p-0">
      <li v-for="item in genres" :key="item.genre">
        <div class="grid grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-2 text-sm cursor-pointer" @click="$emit('toggle', item.genre)">
          <input class="checkbox checkbox-sm" type="checkbox" :checked="selectedGenres.includes(item.genre)" @click.stop="$emit('toggle', item.genre)" />
          <span class="truncate" :title="item.label || item.genre">{{ item.label || item.genre }}</span>
          <span class="text-base-content/70 tabular-nums">{{ item.count }}</span>
        </div>
      </li>
    </ul>
  </aside>
</template>
