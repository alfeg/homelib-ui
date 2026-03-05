<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from "vue"

import { setLocale, useI18nState } from "../../services/i18n"
import type { LibraryMetadata } from "../../types/library"

interface IndexProgress {
    phase: string
    processed: number
    total: number
    percent: number
    etaSeconds?: number | null
    downloadedBytes?: number
    totalBytes?: number | null
}

const props = defineProps<{
    hash: string
    magnetUri: string
    metadata: LibraryMetadata | null
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

const slowLoadingMessage = ref<string | null>(null)

let slowLoadingStartTimeout: ReturnType<typeof setTimeout> | null = null
let slowLoadingRotateTimeout: ReturnType<typeof setTimeout> | null = null

const SLOW_LOADING_HINT_DELAY_MS = 5_000
const SLOW_LOADING_HINT_INTERVAL_MIN_MS = 5_000
const SLOW_LOADING_HINT_INTERVAL_MAX_MS = 8_000

const slowLoadingMessageKeys = [
    "status.loadingCacheHints.libraryStillLoading",
    "status.loadingCacheHints.dontBeNervous",
    "status.loadingCacheHints.sorryForDelay",
    "status.loadingCacheHints.deviceSlowerThanExpected",
    "status.loadingCacheHints.indexOnTheWay",
    "status.loadingCacheHints.almostThere",
    "status.loadingCacheHints.stillWorking",
    "status.loadingCacheHints.thanksForPatience",
] as const

function getSlowLoadingMessages() {
    return slowLoadingMessageKeys.map((key) => t(key))
}

function randomInt(min: number, max: number) {
    return Math.floor(Math.random() * (max - min + 1)) + min
}

function pickNextSlowLoadingMessage() {
    const messages = getSlowLoadingMessages()
    if (messages.length === 0) return null
    if (messages.length === 1) return messages[0]

    let next = messages[randomInt(0, messages.length - 1)]
    while (next === slowLoadingMessage.value) {
        next = messages[randomInt(0, messages.length - 1)]
    }

    return next
}

function clearSlowLoadingTimers() {
    if (slowLoadingStartTimeout) {
        clearTimeout(slowLoadingStartTimeout)
        slowLoadingStartTimeout = null
    }

    if (slowLoadingRotateTimeout) {
        clearTimeout(slowLoadingRotateTimeout)
        slowLoadingRotateTimeout = null
    }
}

function scheduleSlowLoadingHintRotation() {
    clearSlowLoadingTimers()

    slowLoadingRotateTimeout = setTimeout(
        () => {
            slowLoadingMessage.value = pickNextSlowLoadingMessage()
            scheduleSlowLoadingHintRotation()
        },
        randomInt(SLOW_LOADING_HINT_INTERVAL_MIN_MS, SLOW_LOADING_HINT_INTERVAL_MAX_MS),
    )
}

function startSlowLoadingHints() {
    clearSlowLoadingTimers()
    slowLoadingMessage.value = null

    slowLoadingStartTimeout = setTimeout(() => {
        slowLoadingMessage.value = pickNextSlowLoadingMessage()
        scheduleSlowLoadingHintRotation()
    }, SLOW_LOADING_HINT_DELAY_MS)
}

function stopSlowLoadingHints() {
    clearSlowLoadingTimers()
    slowLoadingMessage.value = null
}

const onLocaleToggle = (event: Event) => {
    const checked = (event.target as HTMLInputElement).checked
    setLocale(checked ? "en" : "ru")
}

function formatMegabytes(bytes: number) {
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function formatEta(seconds?: number | null) {
    if (typeof seconds !== "number" || !Number.isFinite(seconds) || seconds <= 0) return null
    const total = Math.round(seconds)
    const minutes = Math.floor(total / 60)
    const secs = total % 60
    return `${String(minutes).padStart(2, "0")}:${String(secs).padStart(2, "0")}`
}

function indexingText(progress: IndexProgress) {
    const eta = formatEta(progress.etaSeconds)
    if (eta) {
        return t("status.indexingWithEta", {
            processed: progress.processed,
            total: progress.total,
            percent: progress.percent,
            eta,
        })
    }

    return t("status.indexing", {
        processed: progress.processed,
        total: progress.total,
        percent: progress.percent,
    })
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
        return indexingText(props.progress)
    }

    if (props.progress.phase === "parsing") {
        return indexingText(props.progress)
    }

    if (props.progress.phase === "loading-cache") {
        return [t("status.loadingCache"), slowLoadingMessage.value].filter(Boolean).join(" ")
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

watch(
    () => props.progress?.phase,
    (phase) => {
        if (phase === "loading-cache") {
            startSlowLoadingHints()
            return
        }

        stopSlowLoadingHints()
    },
    { immediate: true },
)

onBeforeUnmount(() => {
    stopSlowLoadingHints()
})

function lastDescriptionLine() {
    if (!props.metadata?.description) return null
    const lines = props.metadata.description.split(/\r\n|\n|\r/).filter((line) => line.trim())
    return lines.length > 0 ? lines[lines.length - 1] : null
}
</script>

<template>
    <header
        class="mb-4 flex flex-col items-start justify-between gap-4 lg:flex-row"
        data-test="library-controls-root"
    >
        <div class="flex flex-col gap-1">
            <h1
                class="text-2xl font-semibold"
                data-test="library-controls-title"
            >
                {{ t("app.title") }}
            </h1>
            <div
                class="md:divide-base-content/20 flex flex-col gap-1 md:flex-row md:items-start md:gap-0 md:divide-x lg:items-center"
            >
                <p
                    v-if="lastDescriptionLine()"
                    class="text-base-content/80 text-sm md:pr-4"
                    data-test="library-controls-description"
                >
                    {{ lastDescriptionLine() }}
                </p>
                <p
                    class="text-base-content/70 md:px-4"
                    data-test="library-controls-hash"
                >
                    {{ t("common.hash") }}:
                    <a
                        class="hover:text-primary inline-flex items-baseline gap-1 font-mono underline transition-colors"
                        data-test="library-controls-hash-link"
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
                    data-test="library-controls-books-count"
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
                    data-test="library-controls-locale-toggle"
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
                    data-test="library-controls-theme-toggle"
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
            <div class="dropdown dropdown-end">
                <button
                    :aria-label="t('buttons.configuration')"
                    class="btn btn-outline btn-circle btn-sm"
                    data-test="library-controls-configuration-btn"
                    tabindex="0"
                    :title="t('buttons.configuration')"
                    type="button"
                >
                    <svg
                        class="h-4 w-4"
                        fill="currentColor"
                        viewBox="0 0 24 24"
                        xmlns="http://www.w3.org/2000/svg"
                    >
                        <path
                            d="M 9.6660156 2 L 9.1757812 4.5234375 C 8.3516137 4.8342536 7.5947862 5.2699307 6.9316406 5.8144531 L 4.5078125 4.9785156 L 2.171875 9.0214844 L 4.1132812 10.708984 C 4.0386488 11.16721 4 11.591845 4 12 C 4 12.408768 4.0398071 12.832626 4.1132812 13.291016 L 4.1132812 13.292969 L 2.171875 14.980469 L 4.5078125 19.021484 L 6.9296875 18.1875 C 7.5928951 18.732319 8.3514346 19.165567 9.1757812 19.476562 L 9.6660156 22 L 14.333984 22 L 14.824219 19.476562 C 15.648925 19.165543 16.404903 18.73057 17.068359 18.185547 L 19.492188 19.021484 L 21.826172 14.980469 L 19.886719 13.291016 C 19.961351 12.83279 20 12.408155 20 12 C 20 11.592457 19.96113 11.168374 19.886719 10.710938 L 19.886719 10.708984 L 21.828125 9.0195312 L 19.492188 4.9785156 L 17.070312 5.8125 C 16.407106 5.2676813 15.648565 4.8344327 14.824219 4.5234375 L 14.333984 2 L 9.6660156 2 z M 11.314453 4 L 12.685547 4 L 13.074219 6 L 14.117188 6.3945312 C 14.745852 6.63147 15.310672 6.9567546 15.800781 7.359375 L 16.664062 8.0664062 L 18.585938 7.40625 L 19.271484 8.5917969 L 17.736328 9.9277344 L 17.912109 11.027344 L 17.912109 11.029297 C 17.973258 11.404235 18 11.718768 18 12 C 18 12.281232 17.973259 12.595718 17.912109 12.970703 L 17.734375 14.070312 L 19.269531 15.40625 L 18.583984 16.59375 L 16.664062 15.931641 L 15.798828 16.640625 C 15.308719 17.043245 14.745852 17.36853 14.117188 17.605469 L 14.115234 17.605469 L 13.072266 18 L 12.683594 20 L 11.314453 20 L 10.925781 18 L 9.8828125 17.605469 C 9.2541467 17.36853 8.6893282 17.043245 8.1992188 16.640625 L 7.3359375 15.933594 L 5.4140625 16.59375 L 4.7285156 15.408203 L 6.265625 14.070312 L 6.0878906 12.974609 L 6.0878906 12.972656 C 6.0276183 12.596088 6 12.280673 6 12 C 6 11.718768 6.026742 11.404282 6.0878906 11.029297 L 6.265625 9.9296875 L 4.7285156 8.59375 L 5.4140625 7.40625 L 7.3359375 8.0683594 L 8.1992188 7.359375 C 8.6893282 6.9567546 9.2541467 6.6314701 9.8828125 6.3945312 L 10.925781 6 L 11.314453 4 z M 12 8 C 9.8034768 8 8 9.8034768 8 12 C 8 14.196523 9.8034768 16 12 16 C 14.196523 16 16 14.196523 16 12 C 16 9.8034768 14.196523 8 12 8 z M 12 10 C 13.111477 10 14 10.888523 14 12 C 14 13.111477 13.111477 14 12 14 C 10.888523 14 10 13.111477 10 12 C 10 10.888523 10.888523 10 12 10 z"
                        />
                    </svg>
                </button>
                <ul
                    class="menu dropdown-content bg-base-100 rounded-box border-base-300 z-20 mt-2 w-56 border p-2 shadow"
                    data-test="library-controls-configuration-menu"
                    tabindex="0"
                >
                    <li>
                        <button
                            class="text-left"
                            data-test="library-controls-change-library-btn"
                            :disabled="reindexing"
                            type="button"
                            @click="$emit('change-library')"
                        >
                            {{ t("buttons.changeLibrary") }}
                        </button>
                    </li>
                    <li>
                        <button
                            class="text-error text-left"
                            data-test="library-controls-full-reset-btn"
                            type="button"
                            @click="$emit('reset')"
                        >
                            {{ t("buttons.fullReset") }}
                        </button>
                    </li>
                </ul>
            </div>
        </div>
    </header>
    <p
        v-if="progressText()"
        class="text-base-content/70 mb-3"
        data-test="library-controls-progress-text"
    >
        {{ progressText() }}
    </p>
</template>
