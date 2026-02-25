import { useLocalStorage } from "@vueuse/core"

const ENDPOINT_KEY = "mhl-api-endpoint"

/** Default injected at build time; "" in normal build, remote URL in standalone build */
const DEFAULT_ENDPOINT = (import.meta.env.VITE_DEFAULT_ENDPOINT as string) ?? ""

/** Reactive ref — bind directly in components with v-model */
export const apiEndpoint = useLocalStorage<string>(ENDPOINT_KEY, DEFAULT_ENDPOINT)

/** Returns the base URL without trailing slash, or "" for same-origin */
export function getApiBase(): string {
    return (apiEndpoint.value ?? "").replace(/\/+$/, "")
}
