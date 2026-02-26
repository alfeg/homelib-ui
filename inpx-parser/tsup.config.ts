import { defineConfig } from "tsup"

export default defineConfig({
    entry: ["src/index.ts"],
    format: ["esm", "cjs"],
    dts: true,
    clean: true,
    sourcemap: true,
    target: "es2020",
    // No external — fflate is bundled so consumers don't need to install it separately
    // Uncomment the line below if you prefer to keep fflate as a peer dependency:
    // external: ["fflate"],
})
