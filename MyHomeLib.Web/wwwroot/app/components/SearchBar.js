import { defineComponent } from "https://unpkg.com/vue@3/dist/vue.esm-browser.prod.js";

export const SearchBar = defineComponent({
    name: "SearchBar",
    props: {
        modelValue: { type: String, default: "" },
        total: { type: Number, default: 0 },
        filtered: { type: Number, default: 0 }
    },
    emits: ["update:modelValue"],
    template: `
        <section class="search-box">
            <input
                type="search"
                class="search-input"
                :value="modelValue"
                @input="$emit('update:modelValue', $event.target.value)"
                placeholder="Search title, author, series, language..."
            />
            <p class="subtle">Showing {{ filtered }} of {{ total }} books</p>
        </section>
    `
});