import { useLocalStorage } from "@vueuse/core"

const ENDPOINT_KEY = "mhl-api-endpoint"

/** Reactive ref — bind directly in components with v-model */
export const apiEndpoint = useLocalStorage<string>(ENDPOINT_KEY, "")

/** Returns the base URL without trailing slash, or "" for same-origin */
export function getApiBase(): string {
    return (apiEndpoint.value ?? "").replace(/\/+$/, "")
}
