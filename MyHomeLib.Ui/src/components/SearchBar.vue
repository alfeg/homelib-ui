<script setup lang="ts">
import { useI18nState } from "../services/i18n"

const model = defineModel<string>({ required: true })
defineProps<{ total: number; filtered: number }>()
const { t } = useI18nState()
</script>

<template>
    <section
        class="mb-3"
        data-test="search-bar-root"
    >
        <input
            v-model="model"
            class="input input-bordered w-full"
            data-test="search-input"
            name="book-search"
            :placeholder="t('search.placeholder')"
            type="search"
        />
        <p
            class="text-base-content/70 mt-1"
            data-test="search-stats"
        >
            {{ t("search.showingOf", { filtered, total }) }}
        </p>
    </section>
</template>
