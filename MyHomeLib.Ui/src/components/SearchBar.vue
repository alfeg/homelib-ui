<script setup lang="ts">
defineProps<{ modelValue: string; total: number; filtered: number }>();
defineEmits<{ (e: "update:modelValue", value: string): void }>();
</script>

<template>
  <section class="mb-3">
    <input
      type="search"
      class="w-full rounded-lg border border-slate-300 px-3 py-2"
      :value="modelValue"
      @input="$emit('update:modelValue', ($event.target as HTMLInputElement).value)"
      placeholder="Search title, author, series, language..."
    />
    <p class="text-slate-500 mt-1">Showing {{ filtered }} of {{ total }} books</p>
  </section>
</template>
