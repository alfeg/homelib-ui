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
      class="card w-full max-w-3xl bg-base-100 shadow-xl p-8 grid gap-3 border border-base-300"
      :class="isDragActive ? 'border-primary border-2 border-dashed' : ''"
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
        class="input input-bordered w-full text-base"
        :disabled="loading"
        @keyup.enter="onSubmit"
      />

      <div class="flex gap-2 flex-wrap">
        <button class="btn btn-primary" :disabled="loading" @click="onSubmit">
          {{ loading ? "Loading..." : "Open Library" }}
        </button>
        <button class="btn btn-outline btn-primary" :disabled="loading" @click="openFilePicker">
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

      <p class="text-base-content/70">You can also drag and drop a .torrent file anywhere on this card.</p>
      <p v-if="error" class="alert alert-error">{{ error }}</p>
    </div>
  </section>
</template>
