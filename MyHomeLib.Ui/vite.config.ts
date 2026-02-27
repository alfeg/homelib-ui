import vue from "@vitejs/plugin-vue"
import { defineConfig } from "vite"
import { viteSingleFile } from "vite-plugin-singlefile"

// Inline everything — ensures all assets end up in a single index.html
const INLINE_ALL_ASSETS = 100_000_000 // 100 MB upper bound

export default defineConfig({
    plugins: [vue(), viteSingleFile()],
    server: {
        host: true,
        port: 5175,
        proxy: {
            "/api": {
                target: "https://books-api.alfeg.net",
                changeOrigin: true,
                secure: false,
            },
        },
    },
    build: {
        target: "esnext",
        cssCodeSplit: false,
        assetsInlineLimit: INLINE_ALL_ASSETS,
        rollupOptions: {
            output: {
                manualChunks: undefined,
                inlineDynamicImports: true,
            },
        },
    },
    optimizeDeps: ["minisearch", "fflate"],
    test: {
        environment: "node",
    },
})
