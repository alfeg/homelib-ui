<script setup lang="ts">
import { apiEndpoint } from "../../services/endpointStore"
import { useI18nState } from "../../services/i18n"

const { t } = useI18nState()
</script>

<template>
    <div
        class="collapse-arrow border-base-300 rounded-box collapse border text-sm"
        data-test="endpoint-settings-root"
    >
        <input
            class="min-h-0"
            data-test="endpoint-settings-toggle"
            type="checkbox"
        />
        <div
            class="collapse-title min-h-0 py-2 font-medium"
            data-test="endpoint-settings-title"
        >
            ⚙ {{ t("endpoint.title") }}
        </div>
        <div class="collapse-content flex flex-col gap-2 pb-2">
            <p
                class="text-base-content/60 text-xs"
                data-test="endpoint-settings-hint"
            >
                {{ t("endpoint.hint") }}
            </p>
            <div class="flex gap-2">
                <input
                    v-model="apiEndpoint"
                    class="input input-bordered input-sm flex-1"
                    data-test="endpoint-settings-input"
                    :placeholder="t('endpoint.placeholder')"
                    spellcheck="false"
                    type="url"
                />
                <button
                    v-if="apiEndpoint"
                    class="btn btn-ghost btn-sm"
                    data-test="endpoint-settings-clear-btn"
                    :title="t('endpoint.clear')"
                    @click="apiEndpoint = ''"
                >
                    ✕
                </button>
            </div>
        </div>
    </div>
</template>
