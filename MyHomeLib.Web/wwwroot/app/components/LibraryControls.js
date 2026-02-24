import { defineComponent } from "https://unpkg.com/vue@3/dist/vue.esm-browser.prod.js";

export const LibraryControls = defineComponent({
    name: "LibraryControls",
    props: {
        hash: { type: String, required: true },
        metadata: { type: Object, default: null },
        status: { type: String, default: "" },
        progress: { type: Object, default: null },
        hasCache: { type: Boolean, default: false },
        lastUpdatedAt: { type: String, default: "" },
        reindexing: { type: Boolean, default: false }
    },
    computed: {
        progressText() {
            if (!this.progress) return "";

            if (this.progress.phase === "indexing") {
                return `Indexing: ${this.progress.processed}/${this.progress.total} (${this.progress.percent}%)`;
            }

            if (this.progress.phase === "loading-cache") {
                return "Loading local cache...";
            }

            if (this.progress.phase === "loading-backend") {
                return "Fetching library from backend...";
            }

            return "";
        }
    },
    emits: ["reindex", "reset"],
    template: `
        <header class="toolbar">
            <div>
                <h1>MyHomeLib Search</h1>
                <p class="subtle">Hash: <span class="mono">{{ hash }}</span></p>
                <p class="subtle" v-if="metadata">
                    Version {{ metadata.version }} · {{ metadata.totalBooks }} books
                </p>
                <p class="subtle" v-if="lastUpdatedAt">Last update: {{ lastUpdatedAt }}</p>
            </div>
            <div class="toolbar-actions">
                <span class="chip" :class="hasCache ? 'chip-cache' : 'chip-live'">
                    {{ hasCache ? 'cache' : 'backend' }}
                </span>
                <button class="btn" :disabled="reindexing" @click="$emit('reindex')">
                    {{ reindexing ? 'Reindexing...' : 'Reindex' }}
                </button>
                <button class="btn btn-danger" @click="$emit('reset')">Full Reset</button>
            </div>
        </header>
        <p v-if="status" class="status-text">{{ status }}</p>
        <p v-if="progressText" class="subtle">{{ progressText }}</p>
    `
});
