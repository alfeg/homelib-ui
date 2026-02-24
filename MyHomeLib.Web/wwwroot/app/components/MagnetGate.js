import { defineComponent, ref } from "https://unpkg.com/vue@3/dist/vue.esm-browser.prod.js";

export const MagnetGate = defineComponent({
    name: "MagnetGate",
    props: {
        loading: { type: Boolean, default: false },
        error: { type: String, default: "" }
    },
    emits: ["submit", "submit-torrent"],
    setup(props, { emit }) {
        const magnetInput = ref("");
        const fileInput = ref(null);
        const isDragActive = ref(false);

        const onSubmit = () => {
            if (props.loading) return;
            emit("submit", magnetInput.value);
        };

        const openFilePicker = () => {
            fileInput.value?.click();
        };

        const submitTorrent = (file) => {
            if (props.loading) return;

            if (!file) {
                emit("submit-torrent", null);
                return;
            }

            emit("submit-torrent", file);
        };

        const onFileInput = (event) => {
            const selected = event.target?.files?.[0] ?? null;
            submitTorrent(selected);
            if (event.target) {
                event.target.value = "";
            }
        };

        const onDragEnter = () => {
            isDragActive.value = true;
        };

        const onDragLeave = () => {
            isDragActive.value = false;
        };

        const onDrop = (event) => {
            isDragActive.value = false;
            const dropped = event.dataTransfer?.files?.[0] ?? null;
            submitTorrent(dropped);
        };

        return {
            magnetInput,
            fileInput,
            isDragActive,
            onSubmit,
            openFilePicker,
            onFileInput,
            onDragEnter,
            onDragLeave,
            onDrop
        };
    },
    template: `
        <section class="gate-wrap">
            <div
                class="gate-card"
                :class="{ 'gate-card-drag': isDragActive }"
                @dragenter.prevent="onDragEnter"
                @dragover.prevent="onDragEnter"
                @dragleave.prevent="onDragLeave"
                @drop.prevent="onDrop"
            >
                <h1>Connect your library</h1>
                <p>Paste a magnet URI or upload a .torrent file to open this library.</p>

                <input
                    v-model="magnetInput"
                    type="text"
                    placeholder="magnet:?xt=urn:btih:..."
                    class="magnet-input"
                    :disabled="loading"
                    @keyup.enter="onSubmit"
                />

                <div class="gate-actions">
                    <button class="btn btn-primary" :disabled="loading" @click="onSubmit">
                        {{ loading ? "Loading..." : "Open Library" }}
                    </button>
                    <button class="btn" type="button" :disabled="loading" @click="openFilePicker">
                        Choose .torrent file
                    </button>
                </div>

                <input
                    ref="fileInput"
                    type="file"
                    accept=".torrent,application/x-bittorrent"
                    class="torrent-file-input"
                    :disabled="loading"
                    @change="onFileInput"
                />

                <p class="subtle">You can also drag and drop a .torrent file anywhere on this card.</p>
                <p v-if="error" class="error-text">{{ error }}</p>
            </div>
        </section>
    `
});
