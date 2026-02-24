<script setup lang="ts">
const props = defineProps<{
  hash: string;
  metadata: any;
  status: string;
  progress: any;
  hasCache: boolean;
  lastUpdatedAt: string;
  reindexing: boolean;
}>();

defineEmits<{ (e: "reindex"): void; (e: "reset"): void }>();

function formatMegabytes(bytes: number) {
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function progressText() {
  if (!props.progress) return "";

  if (props.progress.phase === "indexing") {
    return `Indexing: ${props.progress.processed}/${props.progress.total} (${props.progress.percent}%)`;
  }

  if (props.progress.phase === "parsing") {
    if (props.progress.total) {
      return `Parsing INPX: ${props.progress.processed}/${props.progress.total} (${props.progress.percent}%)`;
    }

    return `Parsing INPX: ${props.progress.percent}%`;
  }

  if (props.progress.phase === "loading-cache") {
    return "Loading local cache...";
  }

  if (props.progress.phase === "clearing-local") {
    return "Clearing local cache and search index...";
  }

  if (props.progress.phase === "loading-backend") {
    const downloaded = props.progress.downloadedBytes ?? 0;
    const total = props.progress.totalBytes;

    if (total) {
      return `Downloading INPX payload: ${formatMegabytes(downloaded)} / ${formatMegabytes(total)} (${props.progress.percent ?? 0}%)`;
    }

    return `Downloading INPX payload: ${formatMegabytes(downloaded)} downloaded`;
  }

  return "";
}
</script>

<template>
  <header class="flex justify-between gap-4 items-start mb-4 flex-col lg:flex-row">
    <div>
      <h1 class="text-2xl font-semibold">MyHomeLib Search</h1>
      <p class="text-slate-500">Hash: <span class="font-mono">{{ hash }}</span></p>
      <p class="text-slate-500" v-if="metadata">Version {{ metadata.version }} · {{ metadata.totalBooks }} books</p>
      <p class="text-slate-500" v-if="lastUpdatedAt">Last update: {{ lastUpdatedAt }}</p>
    </div>
    <div class="flex items-center gap-2 flex-wrap">
      <span class="text-xs px-2 py-1 rounded-full uppercase tracking-wide" :class="hasCache ? 'bg-emerald-100 text-emerald-700' : 'bg-blue-100 text-blue-700'">
        {{ hasCache ? 'cache' : 'backend' }}
      </span>
      <button class="px-3 py-2 rounded-lg border border-blue-900 text-blue-900 disabled:opacity-60" :disabled="reindexing" @click="$emit('reindex')">
        {{ reindexing ? 'Reindexing...' : 'Reindex' }}
      </button>
      <button class="px-3 py-2 rounded-lg border border-red-700 text-red-700" @click="$emit('reset')">Full Reset</button>
    </div>
  </header>
  <p v-if="status" class="text-slate-700 mb-2">{{ status }}</p>
  <p v-if="progressText()" class="text-slate-500 mb-3">{{ progressText() }}</p>
</template>
