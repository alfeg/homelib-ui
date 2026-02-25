<script setup lang="ts">
import { useI18nState } from "../services/i18n";

defineProps<{ modelValue: string; total: number; filtered: number }>();
defineEmits<{ (e: "update:modelValue", value: string): void }>();
const { t } = useI18nState();
</script>

<template>
  <section class="mb-3">
    <input
      type="search"
      class="input input-bordered w-full"
      :value="modelValue"
      @input="$emit('update:modelValue', ($event.target as HTMLInputElement).value)"
      :placeholder="t('search.placeholder')"
      name="book-search"
    />
    <p class="text-base-content/70 mt-1">{{ t("search.showingOf", { filtered, total }) }}</p>
  </section>
</template>
