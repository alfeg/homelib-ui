import { defineComponent, onMounted } from "https://unpkg.com/vue@3/dist/vue.esm-browser.prod.js";
import { useLibraryState } from "./composables/useLibraryState.js";
import { MagnetGate } from "./components/MagnetGate.js";
import { LibraryControls } from "./components/LibraryControls.js";
import { SearchBar } from "./components/SearchBar.js";
import { BooksTable } from "./components/BooksTable.js";

export const App = defineComponent({
    name: "App",
    components: {
        MagnetGate,
        LibraryControls,
        SearchBar,
        BooksTable
    },
    setup() {
        const state = useLibraryState();
        onMounted(() => state.bootstrap());
        return state;
    },
    template: `
        <main>
            <MagnetGate
                v-if="!isMagnetSet"
                :loading="isLoading"
                :error="error"
                @submit="submitMagnet"
            />

            <section v-else class="page">
                <LibraryControls
                    :hash="magnetHash"
                    :metadata="metadata"
                    :status="status"
                    :progress="indexProgress"
                    :has-cache="hasCache"
                    :last-updated-at="lastUpdatedAt"
                    :reindexing="isReindexing"
                    @reindex="reindexCurrent"
                    @reset="resetAll"
                />

                <SearchBar
                    v-model="searchTerm"
                    :total="books.length"
                    :filtered="filteredBooks.length"
                />

                <p v-if="error" class="error-text">{{ error }}</p>

                <BooksTable
                    :books="filteredBooks"
                    :downloading-by-id="downloadingById"
                    @download="downloadBook"
                />
            </section>
        </main>
    `
});
