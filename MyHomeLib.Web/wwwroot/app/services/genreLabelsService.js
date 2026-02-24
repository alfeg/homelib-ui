const GENRES_JSON_URL = new URL("../genres.json", import.meta.url);

let genresMapPromise = null;

function toLabelMap(payload) {
    const entries = Object.entries(payload ?? {});
    const map = new Map();

    for (let i = 0; i < entries.length; i += 1) {
        const [code, value] = entries[i];
        const normalizedCode = String(code ?? "").trim();
        if (!normalizedCode) continue;

        const friendlyName = typeof value?.name === "string" ? value.name.trim() : "";
        map.set(normalizedCode, friendlyName || normalizedCode);
    }

    return map;
}

export async function loadGenreLabels() {
    if (!genresMapPromise) {
        genresMapPromise = fetch(GENRES_JSON_URL)
            .then(async (response) => {
                if (!response.ok) {
                    throw new Error(`Failed to load genre labels (${response.status}).`);
                }

                const payload = await response.json();
                return toLabelMap(payload);
            })
            .catch((error) => {
                console.warn("genres.json could not be loaded", error);
                return new Map();
            });
    }

    return genresMapPromise;
}
