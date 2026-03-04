<script setup lang="ts">
import { setLocale, useI18nState } from "../services/i18n"
import type { LibraryMetadata } from "../types/library"

interface IndexProgress {
    phase: string
    processed: number
    total: number
    percent: number
    downloadedBytes?: number
    totalBytes?: number | null
}

const props = defineProps<{
    hash: string
    magnetUri: string
    metadata: LibraryMetadata | null
    status: string
    progress: IndexProgress
    hasCache: boolean
    lastUpdatedAt: string
    reindexing: boolean
    theme: string
}>()

defineEmits<{
    (e: "change-library"): void
    (e: "reset"): void
    (e: "theme-toggle", value: boolean): void
}>()
const { t, locale } = useI18nState()

const onLocaleToggle = (event: Event) => {
    const checked = (event.target as HTMLInputElement).checked
    setLocale(checked ? "en" : "ru")
}

function formatMegabytes(bytes: number) {
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function booksCountText(n: number): string {
    if (locale.value === "ru") {
        const mod10 = n % 10
        const mod100 = n % 100
        if (mod10 === 1 && mod100 !== 11) return `${n.toLocaleString("ru")} книга`
        if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) return `${n.toLocaleString("ru")} книги`
        return `${n.toLocaleString("ru")} книг`
    }
    return `${n.toLocaleString("en")} ${n === 1 ? "book" : "books"}`
}

function progressText() {
    if (!props.progress) return ""

    if (props.progress.phase === "indexing") {
        return t("status.indexing", {
            processed: props.progress.processed,
            total: props.progress.total,
            percent: props.progress.percent,
        })
    }

    if (props.progress.phase === "parsing") {
        if (props.progress.total) {
            return t("status.parsingWithTotal", {
                processed: props.progress.processed,
                total: props.progress.total,
                percent: props.progress.percent,
            })
        }

        return t("status.parsingSimple", { percent: props.progress.percent })
    }

    if (props.progress.phase === "loading-cache") {
        return t("status.loadingCache")
    }

    if (props.progress.phase === "clearing-local") {
        return t("status.clearingLocal")
    }

    if (props.progress.phase === "loading-backend") {
        const downloaded = props.progress.downloadedBytes ?? 0
        const total = props.progress.totalBytes

        if (total) {
            return t("status.downloadingInpxTotal", {
                downloaded: formatMegabytes(downloaded),
                total: formatMegabytes(total),
                percent: props.progress.percent ?? 0,
            })
        }

        return t("status.downloadingInpxSimple", { downloaded: formatMegabytes(downloaded) })
    }

    return ""
}

function phaseStatus() {
    const phase = props.progress?.phase ?? "idle"
    if (phase === "ready") {
        return { text: t("common.phaseReady"), cls: "badge-success" }
    }

    if (phase === "indexing") {
        return { text: t("common.phaseIndexing"), cls: "badge-warning" }
    }

    if (phase === "idle") {
        return { text: t("common.phaseInit"), cls: "badge-neutral" }
    }

    return { text: t("common.phaseLoading"), cls: "badge-info" }
}

function lastDescriptionLine() {
    if (!props.metadata?.description) return null
    const lines = props.metadata.description.split(/\r\n|\n|\r/).filter((line) => line.trim())
    return lines.length > 0 ? lines[lines.length - 1] : null
}
</script>

<template>
    <header class="mb-4 flex flex-col items-start justify-between gap-4 lg:flex-row">
        <div class="flex flex-col gap-1">
            <h1 class="text-2xl font-semibold">{{ t("app.title") }}</h1>
            <div
                class="md:divide-base-content/20 flex flex-col gap-1 md:flex-row md:items-start md:gap-0 md:divide-x lg:items-center"
            >
                <p
                    v-if="lastDescriptionLine()"
                    class="text-base-content/80 text-sm md:pr-4"
                >
                    {{ lastDescriptionLine() }}
                </p>
                <p class="text-base-content/70 md:px-4">
                    {{ t("common.hash") }}:
                    <a
                        class="hover:text-primary inline-flex items-baseline gap-1 font-mono underline transition-colors"
                        :href="magnetUri"
                        :title="magnetUri"
                    >
                        <svg
                            class="inline-block h-4 w-4 shrink-0"
                            fill="none"
                            stroke-linecap="round"
                            stroke-linejoin="round"
                            stroke-width="2.5"
                            viewBox="0 -6 24 24"
                            xmlns="http://www.w3.org/2000/svg"
                        >
                            <!-- Left pole (red) - mirrored -->
                            <path
                                d="M12 15a4 4 0 0 1 -4-4V3h-2v8a6 6 0 0 0 6 6"
                                stroke="#ef4444"
                            />
                            <!-- Right pole (blue) -->
                            <path
                                d="M12 15a4 4 0 0 0 4-4V3h2v8a6 6 0 0 1-6 6"
                                stroke="#3b82f6"
                            />
                        </svg>
                        {{ hash }}
                    </a>
                </p>
                <p
                    v-if="metadata"
                    class="text-base-content/70 md:pl-4"
                >
                    {{ booksCountText(metadata.totalBooks) }}
                </p>
                <!-- <p
                    v-if="lastUpdatedAt"
                    class="text-base-content/70 md:pl-4"
                >
                    {{ t("common.lastUpdate", { value: lastUpdatedAt }) }}
                </p> -->
            </div>
        </div>
        <div class="flex flex-wrap items-center gap-2">
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
            <label class="swap swap-rotate btn btn-ghost btn-circle btn-sm border-base-300 border p-1">
                <input
                    :checked="theme === 'dark'"
                    class="theme-controller"
                    name="theme-toggle"
                    type="checkbox"
                    value="dark"
                    @change="$emit('theme-toggle', ($event.target as HTMLInputElement).checked)"
                />
                <svg
                    class="swap-off h-8 w-8 fill-current"
                    viewBox="0 0 24 24"
                    xmlns="http://www.w3.org/2000/svg"
                >
                    <path
                        d="M5.64,17l-.71.71a1,1,0,0,0,0,1.41,1,1,0,0,0,1.41,0l.71-.71A1,1,0,0,0,5.64,17ZM5,12a1,1,0,0,0-1-1H3a1,1,0,0,0,0,2H4A1,1,0,0,0,5,12Zm7-7a1,1,0,0,0,1-1V3a1,1,0,0,0-2,0V4A1,1,0,0,0,12,5ZM5.64,7.05a1,1,0,0,0,.7.29,1,1,0,0,0,.71-.29,1,1,0,0,0,0-1.41l-.71-.71A1,1,0,0,0,4.93,6.34Zm12,.29a1,1,0,0,0,.7-.29l.71-.71a1,1,0,1,0-1.41-1.41L17,5.64a1,1,0,0,0,0,1.41A1,1,0,0,0,17.66,7.34ZM21,11H20a1,1,0,0,0,0,2h1a1,1,0,0,0,0-2Zm-9,8a1,1,0,0,0-1,1v1a1,1,0,0,0,2,0V20A1,1,0,0,0,12,19ZM18.36,17A1,1,0,0,0,17,18.36l.71.71a1,1,0,0,0,1.41,0,1,1,0,0,0,0-1.41ZM12,6.5A5.5,5.5,0,1,0,17.5,12,5.51,5.51,0,0,0,12,6.5Zm0,9A3.5,3.5,0,1,1,15.5,12,3.5,3.5,0,0,1,12,15.5Z"
                    />
                </svg>

                <svg
                    class="swap-on h-8 w-8 fill-current"
                    viewBox="0 0 24 24"
                    xmlns="http://www.w3.org/2000/svg"
                >
                    <path
                        d="M21.64,13a1,1,0,0,0-1.05-.14,8.05,8.05,0,0,1-3.37.73A8.15,8.15,0,0,1,9.08,5.49a8.59,8.59,0,0,1,.25-2A1,1,0,0,0,8,2.36,10.14,10.14,0,1,0,22,14.05,1,1,0,0,0,21.64,13Zm-9.5,6.69A8.14,8.14,0,0,1,7.08,5.22v.27A10.15,10.15,0,0,0,17.22,15.63a9.79,9.79,0,0,0,2.1-.22A8.11,8.11,0,0,1,12.14,19.73Z"
                    />
                </svg>
            </label>
            <span
                class="badge badge-sm tracking-wide uppercase"
                :class="phaseStatus().cls"
            >
                {{ phaseStatus().text }}
            </span>
            <button
                class="btn btn-outline btn-secondary btn-sm"
                :disabled="reindexing"
                @click="$emit('change-library')"
            >
                {{ t("buttons.changeLibrary") }}
            </button>
            <button
                class="btn btn-outline btn-error btn-sm"
                @click="$emit('reset')"
            >
                {{ t("buttons.fullReset") }}
            </button>
        </div>
    </header>
    <p
        v-if="status"
        class="text-base-content mb-2"
    >
        {{ status }}
    </p>
    <p
        v-if="progressText()"
        class="text-base-content/70 mb-3"
    >
        {{ progressText() }}
    </p>
</template>
