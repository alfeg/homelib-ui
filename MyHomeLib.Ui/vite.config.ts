import vue from "@vitejs/plugin-vue"
import { defineConfig } from "vite"

export default defineConfig({
    plugins: [vue()],
    server: {
        proxy: {
            "/api": {
                target: "https://books-api.alfeg.net",
                changeOrigin: true,
                secure: false,
            },
        },
    },
    optimizeDeps: ["minisearch", "fflate"],
    test: {
        environment: "node",
    },
})
