<script setup lang="ts">
const props = defineProps<{
    checked: boolean
    dataTest: string
}>()

const emit = defineEmits<{
    (e: "toggle", checked: boolean): void
}>()

function onChange(event: Event) {
    emit("toggle", (event.target as HTMLInputElement).checked)
}
</script>

<template>
    <label class="swap btn btn-ghost btn-sm border-base-300 border px-2">
        <input
            :checked="props.checked"
            :data-test="props.dataTest"
            name="locale-toggle"
            type="checkbox"
            @change="onChange"
        />
        <span class="swap-off font-semibold">RU</span>
        <span class="swap-on font-semibold">EN</span>
    </label>
</template>
