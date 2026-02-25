import { useLocalStorage } from "@vueuse/core"

const ENDPOINT_KEY = "mhl-api-endpoint"

const IS_STANDALONE = (import.meta.env.VITE_STANDALONE as string) === "true"

/** Default injected at build time; "" in normal build, remote URL in standalone build */
const DEFAULT_ENDPOINT = (import.meta.env.VITE_DEFAULT_ENDPOINT as string) ?? ""

/** Reactive ref — bind directly in components with v-model.
 *  In non-standalone builds always stays empty (same-origin). */
export const apiEndpoint = IS_STANDALONE ? useLocalStorage<string>(ENDPOINT_KEY, DEFAULT_ENDPOINT) : { value: "" }

/** Returns the base URL without trailing slash, or "" for same-origin */
export function getApiBase(): string {
    if (!IS_STANDALONE) return ""
    return (apiEndpoint.value ?? "").replace(/\/+$/, "")
}

export { IS_STANDALONE }
