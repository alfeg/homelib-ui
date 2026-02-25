<script setup lang="ts">
import { useI18nState } from "../services/i18n"

const model = defineModel<string>({ required: true })
defineProps<{ total: number; filtered: number }>()
const { t } = useI18nState()
</script>

<template>
    <section class="mb-3">
        <input
            v-model="model"
            class="input input-bordered w-full"
            name="book-search"
            :placeholder="t('search.placeholder')"
            type="search"
        />
        <p class="text-base-content/70 mt-1">{{ t("search.showingOf", { filtered, total }) }}</p>
    </section>
</template>
