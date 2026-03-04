<script setup lang="ts">
import { computed, ref } from "vue"

import { IS_STANDALONE } from "../services/endpointStore"
import { setLocale, useI18nState } from "../services/i18n"
import EndpointSettings from "./EndpointSettings.vue"

const props = defineProps<{
    loading: boolean
    error: string
    progress?: {
        phase: string
        percent: number
        processed?: number
        total?: number
        downloadedBytes?: number
        totalBytes?: number | null
    }
    progressLabel?: string
}>()
const emit = defineEmits<{
    (e: "submit", value: string): void
    (e: "submit-torrent", file: File | null): void
    (e: "dismiss"): void
}>()

const magnetInput = ref("")
const fileInput = ref<HTMLInputElement | null>(null)
const isDragActive = ref(false)
const { t, locale } = useI18nState()

const LUCKY_MAGNET =
    "magnet:?xt=urn:btih:2072C0F450A333C84B06AFD482BB563664D36398&tr=http%3A%2F%2Fbt.booktracker.work%2Fann%3Fmagnet"

const onSubmit = () => {
    if (props.loading) return
    emit("submit", magnetInput.value)
}

const onLucky = () => {
    if (props.loading) return
    emit("submit", LUCKY_MAGNET)
}

const isLoadingMode = computed(() => props.loading || (!!props.progress && props.progress.phase !== "idle"))

const openFilePicker = () => fileInput.value?.click()

const submitTorrent = (file: File | null) => {
    if (props.loading) return
    emit("submit-torrent", file)
}

const onFileInput = (event: Event) => {
    const target = event.target as HTMLInputElement
    submitTorrent(target.files?.[0] ?? null)
    target.value = ""
}

const onLocaleToggle = (event: Event) => {
    const checked = (event.target as HTMLInputElement).checked
    setLocale(checked ? "en" : "ru")
}
</script>

<template>
    <section
        class="flex min-h-screen items-center justify-center p-4"
        data-test="magnet-gate-root"
    >
        <div
            class="card bg-base-100 border-base-300 grid w-full max-w-3xl gap-3 border p-8 shadow-xl"
            data-test="magnet-gate-card"
            :class="!isLoadingMode && isDragActive ? 'border-primary border-2 border-dashed' : ''"
            @dragenter.prevent="!isLoadingMode && (isDragActive = true)"
            @dragleave.prevent="isDragActive = false"
            @dragover.prevent="!isLoadingMode && (isDragActive = true)"
            @drop.prevent="
                (e) => {
                    isDragActive = false
                    if (!isLoadingMode) submitTorrent((e.dataTransfer?.files?.[0] as File) ?? null)
                }
            "
        >
            <h1
                class="text-2xl font-semibold"
                data-test="magnet-gate-title"
            >
                {{ isLoadingMode ? t("gate.titleLoading") : t("gate.title") }}
            </h1>
            <div class="flex items-center justify-between gap-2">
                <p
                    class="text-slate-500"
                    data-test="magnet-gate-subtitle"
                >
                    {{ isLoadingMode ? "" : t("gate.subtitle") }}
                </p>
                <label class="swap btn btn-ghost btn-sm border-base-300 border px-2">
                    <input
                        :checked="locale === 'en'"
                        data-test="magnet-gate-locale-toggle"
                        name="locale-toggle"
                        type="checkbox"
                        @change="onLocaleToggle"
                    />
                    <span class="swap-off font-semibold">RU</span>
                    <span class="swap-on font-semibold">EN</span>
                </label>
            </div>

            <!-- Loading mode: show progress -->
            <template v-if="isLoadingMode">
                <div class="flex flex-col items-center gap-4 py-4">
                    <span
                        v-if="!error"
                        class="loading loading-spinner loading-lg text-primary"
                        data-test="magnet-gate-spinner"
                    />
                    <progress
                        class="progress progress-primary w-full"
                        data-test="magnet-gate-progress"
                        max="100"
                        :value="progress?.percent || undefined"
                    />
                    <p
                        class="text-base-content/70 text-center text-sm"
                        data-test="magnet-gate-progress-text"
                    >
                        {{ progressLabel || t("gate.loading") }}
                    </p>
                    <div
                        v-if="error"
                        class="flex w-full flex-col gap-3"
                    >
                        <p
                            class="alert alert-error"
                            data-test="magnet-gate-error"
                        >
                            {{ error }}
                        </p>
                        <button
                            class="btn btn-outline btn-sm self-start"
                            data-test="magnet-gate-dismiss-btn"
                            @click="emit('dismiss')"
                        >
                            {{ t("gate.dismiss") }}
                        </button>
                    </div>
                </div>
            </template>

            <!-- Input mode: show form -->
            <template v-else>
                <input
                    v-model="magnetInput"
                    class="input input-bordered w-full text-base"
                    data-test="magnet-gate-input"
                    :disabled="loading"
                    placeholder="magnet:?xt=urn:btih:..."
                    type="text"
                    @keyup.enter="onSubmit"
                />

                <div class="flex flex-col gap-2 sm:flex-row sm:flex-wrap">
                    <button
                        class="btn btn-primary w-full sm:w-auto"
                        data-test="magnet-gate-open-btn"
                        :disabled="loading"
                        @click="onSubmit"
                    >
                        {{ loading ? t("gate.loading") : t("gate.openLibrary") }}
                    </button>
                    <button
                        class="btn btn-outline btn-primary w-full sm:w-auto"
                        data-test="magnet-gate-choose-torrent-btn"
                        :disabled="loading"
                        @click="openFilePicker"
                    >
                        {{ t("gate.chooseTorrent") }}
                    </button>
                    <button
                        class="btn btn-outline w-full sm:w-auto"
                        data-test="magnet-gate-lucky-btn"
                        :disabled="loading"
                        @click="onLucky"
                    >
                        🍀 {{ t("gate.lucky") }}
                    </button>
                </div>

                <input
                    ref="fileInput"
                    accept=".torrent,application/x-bittorrent"
                    class="hidden"
                    data-test="magnet-gate-file-input"
                    :disabled="loading"
                    type="file"
                    @change="onFileInput"
                />

                <p
                    class="text-base-content/70"
                    data-test="magnet-gate-drag-hint"
                >
                    {{ t("gate.dragHint") }}
                </p>
                <p
                    v-if="error"
                    class="alert alert-error"
                    data-test="magnet-gate-error"
                >
                    {{ error }}
                </p>
                <EndpointSettings v-if="IS_STANDALONE" />
            </template>
        </div>
    </section>
</template>
