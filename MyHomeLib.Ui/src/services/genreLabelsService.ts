import genresRu from "../assets/genres.ru.json";
import genresEn from "../assets/genres.en.json";
import { getCurrentLocale } from "./i18n";

const genresMapByLocale = new Map<string, Promise<Map<string, string>>>();

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
  const locale = getCurrentLocale();
  if (!genresMapByLocale.has(locale)) {
    const payload = locale === "en"
      ? (genresEn as Record<string, { name?: string }>)
      : (genresRu as Record<string, { name?: string }>);
    genresMapByLocale.set(locale, Promise.resolve(toLabelMap(payload)));
  }

  return genresMapByLocale.get(locale)!;
}
