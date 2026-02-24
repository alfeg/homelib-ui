import genresData from "../assets/genres.json";

let genresMapPromise: Promise<Map<string, string>> | null = null;

function toLabelMap(payload: Record<string, { name?: string }>) {
  const entries = Object.entries(payload ?? {});
  const map = new Map<string, string>();

  for (const [code, value] of entries) {
    const normalizedCode = String(code ?? "").trim();
    if (!normalizedCode) continue;

    const friendlyName = typeof value?.name === "string" ? value.name.trim() : "";
    map.set(normalizedCode, friendlyName || normalizedCode);
  }

  return map;
}

export async function loadGenreLabels() {
  if (!genresMapPromise) {
    genresMapPromise = Promise.resolve(toLabelMap(genresData as Record<string, { name?: string }>));
  }

  return genresMapPromise;
}
