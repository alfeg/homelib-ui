<script setup lang="ts">
import { ref } from "vue";

const props = defineProps<{ loading: boolean; error: string }>();
const emit = defineEmits<{
  (e: "submit", value: string): void;
  (e: "submit-torrent", file: File | null): void;
}>();

const magnetInput = ref("");
const fileInput = ref<HTMLInputElement | null>(null);
const isDragActive = ref(false);

const onSubmit = () => {
  if (props.loading) return;
  emit("submit", magnetInput.value);
};

const openFilePicker = () => fileInput.value?.click();

const submitTorrent = (file: File | null) => {
  if (props.loading) return;
  emit("submit-torrent", file);
};

const onFileInput = (event: Event) => {
  const target = event.target as HTMLInputElement;
  submitTorrent(target.files?.[0] ?? null);
  target.value = "";
};
</script>

<template>
  <section class="min-h-screen flex items-center justify-center p-4">
    <div
      class="w-full max-w-3xl bg-white rounded-2xl shadow-lg p-8 grid gap-3 border"
      :class="isDragActive ? 'border-blue-500 bg-blue-50 border-2 border-dashed' : 'border-slate-200'"
      @dragenter.prevent="isDragActive = true"
      @dragover.prevent="isDragActive = true"
      @dragleave.prevent="isDragActive = false"
      @drop.prevent="(e) => { isDragActive = false; submitTorrent((e.dataTransfer?.files?.[0] as File) ?? null); }"
    >
      <h1 class="text-2xl font-semibold">Connect your library</h1>
      <p class="text-slate-500">Paste a magnet URI or upload a .torrent file to open this library.</p>

      <input
        v-model="magnetInput"
        type="text"
        placeholder="magnet:?xt=urn:btih:..."
        class="w-full text-base px-4 py-3 border border-slate-300 rounded-lg"
        :disabled="loading"
        @keyup.enter="onSubmit"
      />

      <div class="flex gap-2 flex-wrap">
        <button class="px-3 py-2 rounded-lg bg-blue-900 text-white disabled:opacity-60" :disabled="loading" @click="onSubmit">
          {{ loading ? "Loading..." : "Open Library" }}
        </button>
        <button class="px-3 py-2 rounded-lg border border-blue-900 text-blue-900 disabled:opacity-60" :disabled="loading" @click="openFilePicker">
          Choose .torrent file
        </button>
      </div>

      <input
        ref="fileInput"
        type="file"
        accept=".torrent,application/x-bittorrent"
        class="hidden"
        :disabled="loading"
        @change="onFileInput"
      />

      <p class="text-slate-500">You can also drag and drop a .torrent file anywhere on this card.</p>
      <p v-if="error" class="text-red-700">{{ error }}</p>
    </div>
  </section>
</template>
