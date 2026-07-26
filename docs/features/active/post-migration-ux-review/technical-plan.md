# Technical Plan — Post-Migration UX & Feature-Parity Review

## How to use this plan

Every finding in [index.md](index.md) came from a code-reading pass (git history diff + static
read of the current React/sidecar code), not from running the app. **Nothing here should be fixed
blind.** Each item below has two steps in this order:

1. **Verify** — reproduce the gap against the running app (demo mode is enough unless noted) and
   confirm the cited file/line still shows the problem; the codebase is actively changing under
   another in-progress session, so line numbers may have drifted or a fix may have already landed.
2. **Fix** — apply the proposal from `index.md`, or a better one found during verification.

Do not skip straight to "Fix" — several `aks-migration-fixes`/`service-bus-migration-fixes` items
already show line numbers move fast on this branch.

Work in phases; each phase is independently shippable. Do not start a later phase before the
previous phase's verify+fix pairs are checked off, except where explicitly marked parallelizable.

---

## Phase 1 — Critical: misleading UI and security (do first, no exceptions)

These present as functional but are wired to nothing, silently wrong, or a security regression.
Each needs a scope decision before "fix" is meaningful.

### 1.1 Auth secrets stored in plaintext (index.md #5)

- **Verify:** open the API Client, set a Bearer/API-key auth on a request, save, then open
  `collections.json` on disk and confirm the raw secret is present. Confirm
  `SidecarAuthHeaderBuilder.cs` falls back to using `CredentialKey` as the literal secret.
- **Fix:** add a persisted secret-store endpoint (Tauri-side OS keychain/DPAPI, not the sidecar's
  in-memory dict — check `ICredentialStore` from the old MAUI app for the interface shape to reuse).
  Auth inputs save-on-blur to this endpoint; only an opaque key goes into the request tree/
  `collections.json`. Add a one-time migration note (not a silent auto-migration) for any secret
  already sitting in an existing `collections.json` from this branch's testing.
- **Priority:** highest in this entire plan — treat as a security fix, land before touching anything
  else in `RequestEditor.tsx`.

### 1.2 Monitoring: scope decision, then rebuild or drop (index.md #1)

- **Verify:** open Monitoring, create a rule, reload the app, confirm it's gone (no persistence);
  grep `src-sidecar/Endpoints/` for any monitoring/alert route to confirm none exists.
- **Decide first (ask the user if unclear):** mirror the Observability/DevOps precedent — is
  Monitoring worth a real rebuild, or should it be dropped like Observability/DevOps/Releases were?
  The MAUI version was a real 5-component system (structured per-source rule editor, grouped list
  with live status, SSE-style history) — rebuilding is a multi-day effort, not a quick fix.
- **If rebuild:** split into its own feature folder (`monitoring-rebuild`) with its own index/
  technical-plan/test-plan before starting — this plan only carries the scope decision, not the
  implementation detail.
- **If drop:** remove `MonitoringPage.tsx` and its nav entry, delete any now-dead demo data, and
  update `docs/features/README.md`'s feature order the same way the Observability/DevOps drop was
  recorded in `demo-mode-parity`.

### 1.3 Redis Pub/Sub calls nonexistent routes (index.md #2)

- **Verify:** open Redis → Pub/Sub tab, try Subscribe/Publish, confirm nothing happens; grep
  `RedisEndpoints.cs` for `pubsub` to confirm no route exists.
- **Fix, pick one:** (a) hide the tab behind a "not yet available" state until backed by real
  endpoints, or (b) implement `GET .../pubsub/channels` + an SSE bridge over
  `StackExchange.Redis`'s subscriber API. Recommend (a) first to stop the misleading UI immediately,
  file (b) as a smaller follow-up if wanted.

### 1.4 Redis "Export Selected" exports wrong data (index.md #3)

- **Verify:** select 3+ keys of different types, export, open the file, confirm every entry shows
  the same type/TTL as whichever key's detail panel was last open, and no value is present.
- **Fix:** add `POST /api/redis/{cacheId}/keys/export` that fetches type+value per key
  server-side; point the export button at it instead of building the payload client-side from a
  single cached query result.

### 1.5 Storage mutation endpoints are all dead, Metadata Save silently no-ops (index.md #4)

- **Verify:** grep `src-sidecar/Endpoints/StorageEndpoints.cs` for `upload`/`copy`/`versions`/
  `sas`/`undelete`/`metadata` routes (expect none); in the app, edit blob metadata, click Save,
  reload, confirm the edit didn't persist.
- **Fix:** wire the five capabilities (`UploadBlobAsync`, `CopyBlobAsync`, `SetBlobMetadataAsync`,
  `ListBlobVersionsAsync`/`RestoreBlobVersionAsync`, `GetBlobSasUrlAsync`) to new sidecar routes —
  they're already implemented on `IStorageClient`/`DemoStorageClient`, this is endpoint registration,
  not new business logic. Until each route lands, `disable` (not silently no-op) the corresponding
  button with a tooltip explaining why.
- **Sequencing:** Metadata Save is the most dangerous sub-item (looks like it worked) — land that
  route first even if the others are staged later.
- **Consider its own folder** (`storage-mutation-endpoints`) if it grows beyond a day of work once
  scoped.

---

## Phase 2 — Quick wins / dead-code cleanup (parallelizable, low risk)

### 2.1 Remove `DevOpsSettings` dead tab (index.md #24)
- **Verify:** confirm Save has no handler and no sidecar route backs it.
- **Fix:** delete the tab, its import in `SettingsPage.tsx`, and the file — this is pure
  follow-through on the already-made 2026-07-26 DevOps-drop decision, not a new call.

### 2.2 Fix or remove dead Appearance controls (index.md #25)
- **Verify:** toggle Font Size / Density, confirm nothing visibly changes.
- **Fix:** wire to the settings store + CSS custom properties, or remove until implemented.

### 2.3 API Client Capture Rules tab is a non-functional mockup (index.md #7)
- **Verify:** add a capture rule, switch requests, switch back — confirm it's gone.
- **Fix:** wire to `request.captureRules` on read and write; restore Header/StatusCode sources;
  add an inline "preview against last response" action.

### 2.4 Destructive-action mutations have no error feedback (index.md #27)
- **Verify:** trigger an AKS restart/scale/delete against an invalid target (or simulate a 500) and
  confirm nothing appears in the UI.
- **Fix:** add a generic `onError` to the shared mutation options (AKS first, then check Service
  Bus/Redis/Storage hooks for the same gap) that toasts the server's error message via the existing
  `NotificationSystem.tsx`.

---

## Phase 3 — Feature-level gaps (sequence within a domain; domains are parallelizable)

### API Client (index.md #6, #8, #9, #10, #11)
1. Verify each against the live app (body editor has no highlighting; variable generator is a flat
   grid; Ctrl+K has no per-request entries; Git panel is single-repo; no conflict banner on
   external edits).
2. Fix order: body editor (CodeMirror6) → variable generator (inline mode toggle) → command-palette
   integration (also fixes #9 and Shell #23 together, do once) → Git multi-repo picker → conflict
   toast. Git multi-repo and conflict-resolution are lower priority — confirm with the user whether
   multi-root linked collections are still an active workflow before investing here.

### Redis (index.md #12, #13, #14, #15)
1. Verify: Keyspace Health duplicates Server Info; Prefix Memory shows counts not bytes; hash/list/
   set/zset views have no edit controls; namespace tree caps at 20 children with no lazy load.
2. Fix order: mutation routes for hash/list/set/zset (highest real-world debugging value) →
   keyspace health analyzer port → prefix memory bytes → namespace tree recursion/virtualization
   (largest, do last).

### Storage (index.md #16, #17, #18, #19 — beyond the Phase 1.5 wiring)
1. Verify: upload is textarea-only; no version-diff UI at all; mutation buttons render regardless
   of `allowMutations`; copy destination is free-text.
2. Fix order: `allowMutations` enforcement (safety, do first) → real file upload with
   `react-dropzone` → version diff pane → copy-dialog container picker + overwrite guard.

### Dashboard (index.md #20, #21)
1. Verify: confirm no pin/favorite mechanism exists anywhere in `DashboardPage.tsx`; confirm
   Settings has no readiness summary.
2. Fix: "Pin to dashboard" affordance on resource rows (AKS/SB/Redis/Storage) → "Getting started"
   checklist in Settings' General tab. Coordinate with `dashboard-redesign` before starting — check
   its current status first, this may already be in scope there.

### Shell / Layout (index.md #22, #23)
1. Verify: confirm the footer status bar has only one global dot; confirm command palette is a
   static 9-item list.
2. Fix: live per-area health strip (reuse each page's existing health queries) → command palette
   fuzzy search + MRU + resource registry (shared win with API Client #9).

### AKS / Service Bus UX polish (index.md #26, #28, #29, #30)
1. Verify each independently — these are additive, not blocking.
2. Fix order: shared `<ResizablePanel>` component (reusable across all of AKS/SB, do first since
   everything else touching these panels benefits) → Pods grid resource-usage column → URL-driven
   drill-down state → Service Bus bulk DLQ actions (lowest priority, explicitly a "nice to have"
   not a regression).

---

## Cross-cutting notes

- Check `aks-migration-fixes` and `service-bus-migration-fixes` status before starting any AKS/SB
  item in this plan — the other in-progress session may have already touched the same files.
- Any new sidecar route added anywhere in this plan should follow whatever CORS/auth pattern
  `tauri-security-hardening` lands on, not the pre-hardening pattern.
- Nothing in this plan should be marked done without the Verify step actually being run against the
  demo-mode app at minimum; prefer a real cluster/namespace/cache for AKS/Redis/Storage items where
  available.
