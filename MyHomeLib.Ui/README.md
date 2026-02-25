# MyHomeLib.Ui

Vue 3 + TypeScript SPA for [MyHomeLib](../README.md). Parses the INPX book index in a Web Worker, builds a [MiniSearch](https://lucaong.github.io/minisearch/) full-text index, persists it in IndexedDB, and handles all search and filter interactions entirely client-side.

## Stack

- **Vue 3** — Composition API, `<script setup>`
- **TypeScript** — strict, no `any`
- **Vite** — dev server + build
- **Tailwind CSS v4** + **DaisyUI** — utility styles and components
- **MiniSearch** — BM25 full-text search in a Web Worker
- **fflate** — streaming INPX ZIP parsing in the worker
- **VueUse** — `createGlobalState` for reactive global state

## Development

```bash
npm install
npm run dev
```

Vite proxies `/api` to `http://localhost:5000` (the .NET backend).

## Production build

```bash
npm run build
```

Output goes to `dist/`. The .NET backend serves it from `wwwroot` (via a static file fallback).

## Key files

| File | Purpose |
|------|---------|
| `src/workers/searchIndex.worker.ts` | Web Worker: parse INPX, build MiniSearch index, search, IndexedDB persistence |
| `src/workers/inpxParser.ts` | INPX streaming parser (fflate) |
| `src/services/searchIndexWorkerClient.ts` | Typed message bridge to the worker |
| `src/composables/useLibraryState.ts` | Global reactive state (search term, filters, results) |
| `src/types/library.ts` | `BookRecord`, `SearchQuery`, `SearchResult` |
| `src/services/i18n.ts` | Russian / English UI strings |
