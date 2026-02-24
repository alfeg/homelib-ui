import { defineComponent, ref } from "https://unpkg.com/vue@3/dist/vue.esm-browser.prod.js";

export const MagnetGate = defineComponent({
    name: "MagnetGate",
    props: {
        loading: { type: Boolean, default: false },
        error: { type: String, default: "" }
    },
    emits: ["submit"],
    setup(_, { emit }) {
        const magnetInput = ref("");

        const onSubmit = () => {
            emit("submit", magnetInput.value);
        };

        return { magnetInput, onSubmit };
    },
    template: `
        <section class="gate-wrap">
            <div class="gate-card">
                <h1>Connect your library</h1>
                <p>Paste a magnet URI to open this library.</p>
                <input
                    v-model="magnetInput"
                    type="text"
                    placeholder="magnet:?xt=urn:btih:..."
                    class="magnet-input"
                    :disabled="loading"
                    @keyup.enter="onSubmit"
                />
                <button class="btn btn-primary" :disabled="loading" @click="onSubmit">
                    {{ loading ? "Loading..." : "Open Library" }}
                </button>
                <p v-if="error" class="error-text">{{ error }}</p>
            </div>
        </section>
    `
});