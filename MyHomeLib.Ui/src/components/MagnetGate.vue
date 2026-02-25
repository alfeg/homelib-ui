<script setup lang="ts">
import { ref } from "vue"

import { setLocale, useI18nState } from "../services/i18n"

const props = defineProps<{ loading: boolean; error: string }>()
const emit = defineEmits<{
    (e: "submit", value: string): void
    (e: "submit-torrent", file: File | null): void
}>()

const magnetInput = ref("")
const fileInput = ref<HTMLInputElement | null>(null)
const isDragActive = ref(false)
const { t, locale } = useI18nState()

const LUCKY_MAGNET =
    "magnet:?xt=urn:btih:F3B650F7CEF06DC01FF6B1AA3DFCDCBD88B78765&tr=http%3A%2F%2Fbt.booktracker.work%2Fann%3Fmagnet"

const onSubmit = () => {
    if (props.loading) return
    emit("submit", magnetInput.value)
}

const onLucky = () => {
    if (props.loading) return
    emit("submit", LUCKY_MAGNET)
}

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
    <section class="flex min-h-screen items-center justify-center p-4">
        <div
            class="card bg-base-100 border-base-300 grid w-full max-w-3xl gap-3 border p-8 shadow-xl"
            :class="isDragActive ? 'border-primary border-2 border-dashed' : ''"
            @dragenter.prevent="isDragActive = true"
            @dragleave.prevent="isDragActive = false"
            @dragover.prevent="isDragActive = true"
            @drop.prevent="
                (e) => {
                    isDragActive = false
                    submitTorrent((e.dataTransfer?.files?.[0] as File) ?? null)
                }
            "
        >
            <h1 class="text-2xl font-semibold">{{ t("gate.title") }}</h1>
            <div class="flex items-center justify-between gap-2">
                <p class="text-slate-500">{{ t("gate.subtitle") }}</p>
                <label class="swap btn btn-ghost btn-sm border-base-300 border px-2">
                    <input
                        :checked="locale === 'en'"
                        name="locale-toggle"
                        type="checkbox"
                        @change="onLocaleToggle"
                    />
                    <span class="swap-off font-semibold">RU</span>
                    <span class="swap-on font-semibold">EN</span>
                </label>
            </div>

            <input
                v-model="magnetInput"
                class="input input-bordered w-full text-base"
                :disabled="loading"
                placeholder="magnet:?xt=urn:btih:..."
                type="text"
                @keyup.enter="onSubmit"
            />

            <div class="flex flex-wrap gap-2">
                <button
                    class="btn btn-primary"
                    :disabled="loading"
                    @click="onSubmit"
                >
                    {{ loading ? t("gate.loading") : t("gate.openLibrary") }}
                </button>
                <button
                    class="btn btn-outline btn-primary"
                    :disabled="loading"
                    @click="openFilePicker"
                >
                    {{ t("gate.chooseTorrent") }}
                </button>
                <button
                    class="btn btn-outline"
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
                :disabled="loading"
                type="file"
                @change="onFileInput"
            />

            <p class="text-base-content/70">{{ t("gate.dragHint") }}</p>
            <p
                v-if="error"
                class="alert alert-error"
            >
                {{ error }}
            </p>
        </div>
    </section>
</template>
