<script setup lang="ts">
defineProps<{ modelValue: string; total: number; filtered: number }>();
defineEmits<{ (e: "update:modelValue", value: string): void }>();
</script>

<template>
  <section class="mb-3">
    <input
      type="search"
      class="input input-bordered w-full"
      :value="modelValue"
      @input="$emit('update:modelValue', ($event.target as HTMLInputElement).value)"
      placeholder="Search title, author, series, language..."
      name="book-search"
    />
    <p class="text-base-content/70 mt-1">Showing {{ filtered }} of {{ total }} books</p>
  </section>
</template>
