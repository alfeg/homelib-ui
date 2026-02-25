import vue from "@vitejs/plugin-vue"
import { defineConfig } from "vite"

export default defineConfig({
    plugins: [vue()],
    server: {
        proxy: {
            "/api": {
                target: "https://localhost:7080",
                changeOrigin: true,
                secure: false,
            },
        },
    },
})
