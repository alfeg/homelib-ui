<script setup lang="ts">
import { buildInfo, buildInfoLine } from "../services/buildInfo"

const line = buildInfoLine()
</script>

<template>
    <main
        class="bg-base-200 text-base-content min-h-screen transition-all duration-500"
        data-test="app-root"
    >
        <slot />

        <footer
            v-if="line"
            class="text-base-content/60 px-4 pb-3 text-center font-mono text-xs"
            data-test="build-info-footer"
        >
            <template v-if="buildInfo.sourceUrl">
                <a
                    class="hover:text-primary underline"
                    :href="buildInfo.sourceUrl"
                    rel="noopener noreferrer"
                    target="_blank"
                >
                    {{ line }}
                </a>
            </template>
            <template v-else>
                {{ line }}
            </template>
            <span v-if="buildInfo.runId"> • run {{ buildInfo.runId }}</span>
            <span v-if="buildInfo.timestamp"> • {{ buildInfo.timestamp }}</span>
        </footer>
    </main>
</template>
