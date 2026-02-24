# MyHomeLib Rewrite Plan

## Goal
Replace Blazor UI with a simple static Vue app (served as `index.html` + ES modules), centered on search + direct download, while keeping server responsibilities limited and explicit.

## Scope (confirmed)
- This document is planning-only.
- Backend provides no server-rendered UI; it serves static frontend assets only.
- Startup gate behavior:
  - If no magnet URI exists in storage, show only one centered magnet URI input (no other actions/UI).
  - If magnet URI exists, derive torrent hash and continue with hash-bound local DB/index initialization.
- Target UX at this stage:
  - Search page is the main flow.
  - Search results include a **Download** button.
  - After click, row/button enters loading state until XHR returns file to the browser.
- Not part of target stage:
  - Downloads queue UX.
  - Separate Downloads page.
  - Torrent status panel in UI.

## Current State Inventory

### Routed views (current app)
1. `/` — `MyHomeLib.Web/Components/Pages/Home.razor`
   - Search box with debounce and Enter-to-search.
   - Language filter.
   - Results table with per-row download enqueue action.
   - Index loading/failure handling.
2. `/downloads` — `MyHomeLib.Web/Components/Pages/Downloads.razor`
   - Per-user persistent queue table.
   - Pending/downloading/ready/failed statuses.
   - Remove/abort actions.
   - Ready file retrieval from backend endpoint.
3. `/audit` — `MyHomeLib.Web/Components/Pages/Audit.razor`
   - Search/enqueue/download audit log table.

### Shared layout/status views (current app)
1. `Components/Layout/MainLayout.razor`
   - App bar, top-level navigation, theme toggle.
2. `Components/Layout/TorrentStatusPanel.razor`
   - TorrServe connectivity and detailed transfer/cache metrics.
3. `Components/Layout/StatCell.razor`
   - Reusable cell in expanded status grid.

## Current Implementation Details

### Backend/API surface
- `GET /api/session/user-id`
  - Reads/creates `mhl_uid` cookie and returns `{ userId }`.
- `GET /api/download/{jobId:guid}`
  - Validates user cookie + queue ownership.
  - Serves completed file from disk and writes audit event.

### Background services
- `LibraryService`
  - Waits for TorrServe, resolves INPX source (configured file, disk, or torrent download), builds DuckDB search index.
  - Exposes `SearchAsync` and `GetLanguagesAsync`.
- `DownloadQueueService`
  - Persists queue in DuckDB.
  - Runs download worker, tracks active jobs, supports delete/abort.
  - Samples torrent stats and can sleep/wake torrent by idle policy.
- `AuditService`
  - Persists search/enqueue/download events in DuckDB and serves audit history.

### Persistence and session
- Library index: DuckDB file (path by config / inferred from INPX).
- Queue database: `queue.db`.
- Audit database: `audit.db`.
- User identity: long-lived `mhl_uid` cookie.
- Theme preference: `localStorage` via `wwwroot/theme.js`.

## Target Architecture for Rewrite Stage

### Frontend
- Static SPA in `MyHomeLib.Web/wwwroot`:
  - `index.html`
  - plain ES modules
  - Vue app split into proper components/composables/services
  - DaisyUI/Tailwind styling
- Primary view: search screen with inline download actions.
- Per-item loading state for download action (no queue screen).
- Keep layout minimal; do not surface Torrent status panel yet.
- Remove Blazor pages/layout from active UI path.

#### Vue structure requirements
- Do not build a monolithic single file that handles all logic/UI.
- Split by responsibility, for example:
  - `src/main.js` (bootstrap)
  - `src/App.vue` (shell only)
  - `src/components/SearchBar.vue`
  - `src/components/SearchResultsTable.vue`
  - `src/components/ResultRow.vue`
  - `src/components/DownloadButton.vue`
  - `src/composables/useSearch.js`
  - `src/composables/useDownload.js`
  - `src/services/apiClient.js`
  - `src/state/appStore.js` (lightweight shared state)
- Keep async API logic in composables/services, not inline in large view templates.
- Persist FlexSearch documents/index metadata in IndexedDB.

### Backend
- Keep server focused on static file hosting + APIs only:
  - library content API by torrent hash (books array parsed from INPX),
  - direct single-file download endpoint(s),
  - guardrails (size/path validation and safe archive extraction behavior).
- Remove Razor component mapping from runtime path after migration.
- Avoid adding new persistent user-activity data in this stage unless explicitly required.

### Data flow (target stage)
1. On app start, check magnet URI in browser storage.
2. If missing: render a centered full-focus magnet URI input screen with submit action only.
3. After magnet save (or when already present): parse torrent hash.
4. Resolve local books DB by torrent hash:
   - if found, open/use IndexedDB data and load existing FlexSearch index;
   - if not found:
     - request library content from backend by hash,
     - backend parses INPX for that hash and returns books array,
     - persist books/index data in IndexedDB and build FlexSearch FTS index.
5. Enter search view and query local FlexSearch index.
6. User clicks **Download** in result row.
7. Client sends XHR request for selected book.
8. UI shows row/button loading until response completes and browser receives file.

## Per-View Plan (target)

### View 0: Magnet bootstrap gate
- Visibility condition:
  - shown only when magnet URI is absent in storage.
- UI:
  - single large centered input for magnet URI.
  - no search controls, no navigation actions, no secondary controls.
- Action:
  - validate/save magnet URI, parse hash, then continue to DB/index initialization.
- Success criteria:
  - user cannot enter main app flow until magnet URI is set.

### View A: Search/Home (primary)
- Inputs/state:
  - query text
  - optional language/filter controls
  - per-row downloading state
- Actions:
  - search
  - trigger direct download
  - reindex current library (force rebuild from backend books array)
  - full reset (remove magnet URI + clear IndexedDB data, then return to magnet bootstrap gate)
- Success criteria:
  - user can search and download without navigating to another page
  - loading state is visible and cleared on completion/failure
  - user can reindex and reset without server-side UI/state dependencies

### View B: Shared Layout
- Keep only essential app shell (title, optional theme toggle/navigation as needed).
- Exclude torrent status panel from this stage.

### Runtime shell
- Single static entry: `index.html`.
- No Blazor route handling in server runtime.

## Rewrite Steps
1. Rewrite this document sections to reflect current inventory and target static-Vue scope.
2. Define frontend file/module structure for `index.html` + ES module Vue app with multiple focused components/composables.
3. Define storage contract for magnet URI and torrent-hash keying.
4. Define local DB resolution/init rules by torrent hash and index bootstrap behavior.
5. Define backend library-content API contract by hash (books array from INPX parse).
6. Define client-side indexing pipeline using FlexSearch over returned books array.
7. Define IndexedDB schema/keys for books + FlexSearch index persistence by torrent hash.
8. Define user actions for reindex and full reset (magnet removal + IndexedDB clear).
9. Mark `/downloads` and status panel as current-only (not in target stage).
10. Define API contract updates needed for direct download flow from search results.
11. Define UI state model for row-level loading/error handling.
12. Define migration notes for removing Blazor runtime wiring (`AddRazorComponents`, `MapRazorComponents`) while keeping required APIs/static files.
13. Define migration notes for queue/audit dependencies (retain internally vs deprecate in UI).
14. Confirm rollout sequence and acceptance checks before code implementation.

## Risks / Open Decisions
- Large file request safety limits and timeout behavior.
- Browser-side storage/indexing limits for large catalogs.
- Whether audit remains server-side in this stage or is postponed.
- Whether magnet handling is user-supplied per session or preconfigured server-side.

## Acceptance Criteria for Planning Stage
- `docs/AppPlan.md` contains:
  - full current view inventory (routed + shared layout/status),
  - implementation details of existing backend/services/persistence,
  - explicit target architecture: static Vue app served by backend static files (no Blazor UI runtime),
  - explicit modular Vue structure (components/composables/services), with no single monolithic UI file,
  - explicit magnet bootstrap gate behavior (single centered input when magnet is missing),
  - explicit local books DB initialization/reuse by torrent hash,
  - explicit indexing definition: backend returns books array (from INPX by hash), client builds FlexSearch FTS index and persists it in IndexedDB,
  - explicit user controls for reindex and full reset (remove magnet URI + clear local index data),
  - explicit target-stage exclusions (no queue page/status panel),
  - concrete rewrite steps and open decisions.
