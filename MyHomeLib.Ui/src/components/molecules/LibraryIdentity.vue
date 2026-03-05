<script setup lang="ts">
defineProps<{
    title: string
    descriptionLine: string | null
    hashLabel: string
    hash: string
    magnetUri: string
    booksCount: string | null
}>()
</script>

<template>
    <div class="flex flex-col gap-1">
        <h1
            class="text-2xl font-semibold"
            data-test="library-controls-title"
        >
            {{ title }}
        </h1>
        <div
            class="md:divide-base-content/20 flex flex-col gap-1 md:flex-row md:items-start md:gap-0 md:divide-x lg:items-center"
        >
            <p
                v-if="descriptionLine"
                class="text-base-content/80 text-sm md:pr-4"
                data-test="library-controls-description"
            >
                {{ descriptionLine }}
            </p>
            <p
                class="text-base-content/70 md:px-4"
                data-test="library-controls-hash"
            >
                {{ hashLabel }}:
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
                        <path
                            d="M12 15a4 4 0 0 1 -4-4V3h-2v8a6 6 0 0 0 6 6"
                            stroke="#ef4444"
                        />
                        <path
                            d="M12 15a4 4 0 0 0 4-4V3h2v8a6 6 0 0 1-6 6"
                            stroke="#3b82f6"
                        />
                    </svg>
                    {{ hash }}
                </a>
            </p>
            <p
                v-if="booksCount"
                class="text-base-content/70 md:pl-4"
                data-test="library-controls-books-count"
            >
                {{ booksCount }}
            </p>
        </div>
    </div>
</template>
