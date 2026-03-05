<script setup lang="ts">
defineProps<{
    message: string
    dataTest: string
    className?: string
}>()
</script>

<template>
    <p
        class="alert alert-error"
        :class="className"
        :data-test="dataTest"
    >
        {{ message }}
    </p>
</template>
