import { defineComponent } from "https://unpkg.com/vue@3/dist/vue.esm-browser.prod.js";

export const GenreSidebar = defineComponent({
    name: "GenreSidebar",
    props: {
        genres: { type: Array, default: () => [] },
        selectedGenres: { type: Array, default: () => [] }
    },
    emits: ["toggle", "clear"],
    methods: {
        isSelected(genre) {
            return this.selectedGenres.includes(genre);
        }
    },
    template: `
        <aside class="genre-sidebar">
            <header class="genre-sidebar-header">
                <h2 class="genre-sidebar-title">Жанры</h2>
                <button
                    class="btn"
                    :disabled="!selectedGenres.length"
                    @click="$emit('clear')">
                    Сбросить
                </button>
            </header>

            <p class="subtle" v-if="genres.length">Всего жанров: {{ genres.length }}</p>
            <p class="subtle" v-else>Жанры не найдены.</p>

            <ul v-if="genres.length" class="genre-list">
                <li v-for="item in genres" :key="item.genre" class="genre-item">
                    <label class="genre-option">
                        <input
                            type="checkbox"
                            :checked="isSelected(item.genre)"
                            @change="$emit('toggle', item.genre)"
                        />
                        <span class="genre-name">{{ item.label || item.genre }}</span>
                        <span class="genre-count">{{ item.count }}</span>
                    </label>
                </li>
            </ul>
        </aside>
    `
});
