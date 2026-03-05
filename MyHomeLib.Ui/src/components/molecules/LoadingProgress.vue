<script setup lang="ts">
const props = defineProps<{
    prefix: string
    percent?: number
    text: string
    error?: string
    dismissText: string
}>()

const emit = defineEmits<{
    (e: "dismiss"): void
}>()

function testId(suffix: string): string {
    return `${props.prefix}-${suffix}`
}
</script>

<template>
    <div class="flex flex-col items-center gap-4 py-4">
        <span
            v-if="!props.error"
            class="loading loading-spinner loading-lg text-primary"
            :data-test="testId('spinner')"
        />
        <progress
            class="progress progress-primary w-full"
            :data-test="testId('progress')"
            max="100"
            :value="props.percent || undefined"
        />
        <p
            class="text-base-content/70 text-center text-sm"
            :data-test="testId('progress-text')"
        >
            {{ props.text }}
        </p>
        <div
            v-if="props.error"
            class="flex w-full flex-col gap-3"
        >
            <p
                class="alert alert-error"
                :data-test="testId('error')"
            >
                {{ props.error }}
            </p>
            <button
                class="btn btn-outline btn-sm self-start"
                :data-test="testId('dismiss-btn')"
                @click="emit('dismiss')"
            >
                {{ props.dismissText }}
            </button>
        </div>
    </div>
</template>
