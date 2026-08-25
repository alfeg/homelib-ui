<script setup lang="ts">
import { computed } from "vue"

import { IS_STANDALONE, getApiBase } from "../../services/endpointStore"
import { setLocale, useI18nState } from "../../services/i18n"
import EndpointSettings from "../molecules/EndpointSettings.vue"

/**
 * Shown while the browser-side index is being downloaded and built.
 *
 * This replaced the magnet gate: the server decides which library it serves, so there is
 * nothing left to ask the user for. Endpoint settings stay, because a standalone build still
 * has to be pointed at a server.
 */
const props = defineProps<{
    title?: string
    loading: boolean
    error: string
    progress?: {
        phase: string
        percent: number
    }
    progressLabel?: string
}>()

const emit = defineEmits<{ (e: "retry"): void }>()

const { t, locale } = useI18nState()

/** The server-rendered pages live next to the API, which is elsewhere in a standalone build. */
const liteHref = computed(() => `${getApiBase()}/m`)

const percent = computed(() => props.progress?.percent || undefined)

const onLocaleToggle = (event: Event) => {
    const checked = (event.target as HTMLInputElement).checked
    setLocale(checked ? "en" : "ru")
}
</script>

<template>
    <section
        class="flex min-h-screen items-center justify-center p-4"
        data-test="library-loading-root"
    >
        <div
            class="card bg-base-100 border-base-300 grid w-full max-w-3xl gap-3 border p-8 shadow-xl"
            data-test="library-loading-card"
        >
            <div class="flex items-start justify-between gap-2">
                <h1
                    class="text-2xl font-semibold"
                    data-test="library-loading-title"
                >
                    {{ title || t("gate.titleLoading") }}
                </h1>
                <label class="swap btn btn-ghost btn-sm border-base-300 border px-2">
                    <input
                        :checked="locale === 'en'"
                        data-test="library-loading-locale-toggle"
                        name="locale-toggle"
                        type="checkbox"
                        @change="onLocaleToggle"
                    />
                    <span class="swap-off font-semibold">RU</span>
                    <span class="swap-on font-semibold">EN</span>
                </label>
            </div>

            <div class="flex flex-col items-center gap-4 py-4">
                <span
                    v-if="!error"
                    class="loading loading-spinner loading-lg text-primary"
                    data-test="library-loading-spinner"
                />
                <progress
                    class="progress progress-primary w-full"
                    data-test="library-loading-progress"
                    max="100"
                    :value="percent"
                />
                <p
                    class="text-base-content/70 text-center text-sm"
                    data-test="library-loading-progress-text"
                >
                    {{ progressLabel || t("gate.loading") }}
                </p>

                <div
                    v-if="error"
                    class="flex w-full flex-col gap-3"
                >
                    <p
                        class="alert alert-error"
                        data-test="library-loading-error"
                    >
                        {{ error }}
                    </p>
                    <button
                        class="btn btn-outline btn-sm self-start"
                        data-test="library-loading-retry-btn"
                        @click="emit('retry')"
                    >
                        {{ t("gate.retry") }}
                    </button>
                </div>
            </div>

            <!--
                Building the index takes 30-60s in the browser. The server-rendered pages are
                ready immediately and work on anything, so the way out is offered up front
                rather than hidden in a menu.
            -->
            <p
                class="text-base-content/70 text-sm"
                data-test="library-loading-lite-hint"
            >
                {{ t("gate.liteHint") }}
                <a
                    class="link link-primary"
                    data-test="library-loading-lite-link"
                    :href="liteHref"
                >
                    {{ t("gate.liteLink") }}
                </a>
            </p>

            <EndpointSettings v-if="IS_STANDALONE" />
        </div>
    </section>
</template>
