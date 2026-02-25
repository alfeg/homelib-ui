import vue from "@vitejs/plugin-vue"
import { defineConfig } from "vite"
import { viteSingleFile } from "vite-plugin-singlefile"

/**
 * Standalone single-file HTML build.
 * Run: npm run build-standalone
 * Output: dist-standalone/index.html  (fully self-contained, no external deps)
 */
export default defineConfig({
    plugins: [vue(), viteSingleFile()],
    define: {
        "import.meta.env.VITE_DEFAULT_ENDPOINT": JSON.stringify("https://books.alfeg.net"),
    },
    build: {
        target: "esnext",
        outDir: "dist-standalone",
        cssCodeSplit: false,
        assetsInlineLimit: 100_000_000,
        rollupOptions: {
            output: {
                // No code-splitting — everything in one chunk
                manualChunks: undefined,
                inlineDynamicImports: true,
            },
        },
    },
})
