// ESLint flat config for Vue 3 + TypeScript
import { FlatCompat } from "@eslint/eslintrc"
import js from "@eslint/js"
import vueEslintConfigTs from "@vue/eslint-config-typescript"
import eslintPluginPrettierRecommended from "eslint-plugin-prettier/recommended"
import eslintPluginVue from "eslint-plugin-vue"
import path from "node:path"
import { fileURLToPath } from "node:url"

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const compat = new FlatCompat({
    baseDirectory: __dirname,
    recommendedConfig: js.configs.recommended,
    allConfig: js.configs.all,
})

export default [
    ...compat.config(),
    ...eslintPluginVue.configs["flat/recommended"],
    ...vueEslintConfigTs(),
    eslintPluginPrettierRecommended,
    {
        files: ["**/*.js", "**/*.ts", "**/*.vue"],

        rules: {
            "@typescript-eslint/no-explicit-any": "warn",
            "@typescript-eslint/no-unused-vars": "warn",
            "@typescript-eslint/no-require-imports": "warn",
            "@typescript-eslint/ban-ts-comment": "warn",
            "no-inner-declarations": "off",
            "vue/multi-word-component-names": "warn",
            "vue/no-mutating-props": "warn",
            "vue/no-parsing-error": "warn",
            "vue/require-v-for-key": "warn",
            "vue/valid-v-for": "warn",
            "vue/return-in-computed-property": "warn",
            "vue/no-side-effects-in-computed-properties": "warn",
            "vue/require-valid-default-prop": "warn",
            "vue/no-dupe-keys": "warn",
            "vue/no-reserved-component-names": "warn",
            "vue/no-v-html": "off",
            "vue/require-default-prop": "off",
            "vue/attribute-hyphenation": ["error", "never"],

            "vue/html-self-closing": [
                "warn",
                {
                    html: {
                        void: "any",
                    },
                },
            ],

            "vue/attributes-order": [
                "warn",
                {
                    order: [
                        "DEFINITION",
                        "LIST_RENDERING",
                        "CONDITIONALS",
                        "RENDER_MODIFIERS",
                        "GLOBAL",
                        "UNIQUE",
                        "SLOT",
                        "TWO_WAY_BINDING",
                        "OTHER_DIRECTIVES",
                        "OTHER_ATTR",
                        "EVENTS",
                        "CONTENT",
                    ],

                    alphabetical: true,
                },
            ],

            "vue/v-on-event-hyphenation": [
                "error",
                "never",
                {
                    autofix: true,
                },
            ],
        },
    },
]
