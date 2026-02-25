<script setup lang="ts">
const props = defineProps<{
  hash: string;
  metadata: any;
  status: string;
  progress: any;
  hasCache: boolean;
  lastUpdatedAt: string;
  reindexing: boolean;
  theme: string;
}>();

defineEmits<{ (e: "reindex"): void; (e: "reset"): void; (e: "theme-toggle", value: boolean): void }>();

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
      <p class="text-base-content/70">Hash: <span class="font-mono">{{ hash }}</span></p>
      <p class="text-base-content/70" v-if="metadata">Version {{ metadata.version }} · {{ metadata.totalBooks }} books</p>
      <p class="text-base-content/70" v-if="lastUpdatedAt">Last update: {{ lastUpdatedAt }}</p>
    </div>
    <div class="flex items-center gap-2 flex-wrap">
      <label class="swap swap-rotate btn btn-ghost btn-circle btn-sm p-1 border border-base-300">
        <input
          class="theme-controller"
          type="checkbox"
          name="theme-toggle"
          value="dark"
          :checked="theme === 'dark'"
          @change="$emit('theme-toggle', ($event.target as HTMLInputElement).checked)"
        />
        <svg class="swap-off h-5 w-5 fill-current text-warning" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
          <path d="M5.64 17l-.71.71 1.41 1.41.71-.71-1.41-1.41Zm12.02.71-.71-.71-1.41 1.41.71.71 1.41-1.41ZM12 5a1 1 0 0 0 1-1V2a1 1 0 0 0-2 0v2a1 1 0 0 0 1 1Zm7 7a1 1 0 0 0 1 1h2a1 1 0 1 0 0-2h-2a1 1 0 0 0-1 1Zm-7 7a1 1 0 0 0-1 1v2a1 1 0 1 0 2 0v-2a1 1 0 0 0-1-1ZM5 12a1 1 0 0 0-1-1H2a1 1 0 1 0 0 2h2a1 1 0 0 0 1-1Zm.64-6.36L4.22 4.22 2.81 5.64l1.41 1.41 1.42-1.41Zm12.73 1.41 1.41-1.41-1.41-1.42-1.41 1.42 1.41 1.41ZM12 7a5 5 0 1 0 0 10 5 5 0 0 0 0-10Z"/>
        </svg>
        <svg class="swap-on h-5 w-5 fill-current text-info" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
          <path d="M21.64 13a9 9 0 1 1-10.63-10.63 1 1 0 0 1 .54 1.82A7 7 0 1 0 19.82 12a1 1 0 0 1 1.82.54Z"/>
        </svg>
      </label>
      <span class="badge badge-sm uppercase tracking-wide" :class="hasCache ? 'badge-success' : 'badge-info'">
        {{ hasCache ? 'cache' : 'backend' }}
      </span>
      <button class="btn btn-outline btn-primary btn-sm" :disabled="reindexing" @click="$emit('reindex')">
        {{ reindexing ? 'Reindexing...' : 'Reindex' }}
      </button>
      <button class="btn btn-outline btn-error btn-sm" @click="$emit('reset')">Full Reset</button>
    </div>
  </header>
  <p v-if="status" class="text-base-content mb-2">{{ status }}</p>
  <p v-if="progressText()" class="text-base-content/70 mb-3">{{ progressText() }}</p>
</template>
