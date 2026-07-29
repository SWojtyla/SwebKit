# Post-Migration UX & Feature-Parity Review — MAUI → Tauri/React

## Summary

A fresh code-level comparison of the deleted MAUI Blazor UI (`src/SwebKit.App/Components/*` at
commit `85d24ed`, the last commit before the Tauri+React scaffold) against the current
`web/src/components/*` + `src-sidecar/Endpoints/*`, covering every feature area **not** already
covered by an existing active plan. Unlike the existing migration-fixes docs, this review explicitly
looks past "does it 404" toward UX quality and asks what the *best* version of each feature should
look like on a web-native stack — including cases that are worse than a bug: several panels present
fully interactive-looking UI wired to nothing, which is more dangerous than an obviously broken one.

**Jira:** not linked

## Scope

Review of: API Client, Redis, Storage, Dashboard, Shell/Layout, Settings, Monitoring, plus a
UX-quality (not bug-parity) pass over AKS and Service Bus. For each finding: what MAUI had, what's
missing/misleading now, why it matters, and a concrete proposal — not necessarily "port it back",
often a genuine upgrade using patterns MAUI/Blazor couldn't offer (URL-driven state, SSE push,
virtualization, command-palette integration).

## Non-Goals / explicitly dropped (do not re-raise)

- **Observability** (App Insights logs/traces) and **DevOps/Pipelines/Releases** — permanently
  dropped by product decision on 2026-07-26, per `../demo-mode-parity/index.md`. Not revisited here.
- **IncidentTimeline** — never ported, not requested, not in scope.
- Anything already tracked in [aks-migration-fixes](../aks-migration-fixes/index.md),
  [service-bus-migration-fixes](../service-bus-migration-fixes/index.md),
  [demo-mode-parity](../demo-mode-parity/index.md), [dashboard-redesign](../dashboard-redesign/index.md),
  [bruno-import-and-reorder](../bruno-import-and-reorder/index.md), or
  [agent-multimodel-api-client](../agent-multimodel-api-client/index.md) — this review was scoped to
  avoid duplicating those and only adds what they don't cover.

## Relationship to other active plans

This is a **review**, not yet a scheduled implementation plan — it surfaces what's missing so it can
be triaged into the existing per-feature plans above or split into new ones (mirroring their
folder convention) once prioritized. Several findings here are larger than "bug fixes" (Monitoring,
Redis health/pub-sub, Storage mutations) and probably deserve their own feature folder before work
starts.

## Findings — critical first (features that *look* functional but are wired to nothing)

These are worse than a missing feature: a user acting on them believes something happened when it
didn't.

1. **Monitoring is entirely a demo mockup with no backend at all.**
   `web/src/components/monitoring/MonitoringPage.tsx` seeds from hardcoded `demoRules`/`demoEvents`
   arrays and never calls the network — no `useQuery`/`useMutation`, no sidecar route exists
   (`src-sidecar/Endpoints/` has no monitoring/alert file). Every action (create/edit/delete rule,
   acknowledge event, enable/disable) only mutates local React state and evaporates on reload. MAUI's
   version was a real system: `AlertRuleDrawer.razor` had source-aware structured rule editors (AKS
   namespace/threshold pickers loaded live via `IAksClient`, Service Bus entity autocomplete via
   `IServiceBusClient`, Redis/Storage thresholds), `AlertRuleGroups.razor`/`AlertRuleRow.razor` grouped
   rules by source with live Ok/Cooldown/Firing/Error status from `IAlertMonitorService`, and
   `MonitoringAlertHistoryPanel.razor` was a real pushed event feed with snooze. None of that logic —
   rule storage, an evaluation loop, or live status — exists anywhere in `src-sidecar` or `web/src`.
   **Recommendation:** this needs a real rebuild, not a fix: sidecar CRUD + evaluation loop (can reuse
   the conceptual shape of `IAlertMonitorService`/`IAlertRuleRepository` from the old MAUI app), a
   source-aware rule editor, grouped list with live status, and an SSE-based history feed (same
   pattern `PodLogView.tsx` already does correctly). Recommend its own feature folder
   (`monitoring-rebuild`) given the size — this is not a quick pass.

2. **Redis Pub/Sub panel calls sidecar routes that don't exist.**
   `web/src/components/redis/PubSubPanel.tsx` posts to `/pubsub/publish` and opens an `EventSource`
   on `/pubsub/subscribe`; grepping `src-sidecar/Endpoints/RedisEndpoints.cs` finds zero `pubsub`
   routes. A developer debugging cache-invalidation traffic will read "no messages" as "nothing is
   happening" when actually nothing is wired up — actively misleading, worse than an empty state.
   **Recommendation:** either hide the panel until backed by real endpoints, or add
   `GET .../pubsub/channels` + a real SSE bridge over `StackExchange.Redis`'s subscriber API (genuinely
   better than MAUI's polling-only view — live push instead of manual refresh).

3. **Redis "Export Selected" silently exports wrong data for every key but the last one viewed.**
   `RedisPage.tsx`'s `handleExportSelected` builds `exportData[key] = { type, ttl }` from the single
   currently-open detail query for *every* key in the selection, and never fetches or includes the
   actual value. This isn't a simplification, it's a correctness bug that would mislead anyone using
   the export for an audit trail. **Recommendation:** add a sidecar
   `POST /api/redis/{cacheId}/keys/export` that fetches type+value per key server-side (as MAUI did)
   and streams a JSON/NDJSON download.

4. **Storage: Upload/Copy/Metadata-Save/Version-Recovery are all wired to nonexistent sidecar routes,
   and Metadata Save silently discards edits.**
   `src-sidecar/Endpoints/StorageEndpoints.cs` implements exactly 5 read-only `GET` routes. There is no
   `/upload`, `/copy`, `/versions`, `/sas`, `/undelete`, or `/metadata` route, even though
   `DemoStorageClient.cs`/`IStorageClient` fully implement all of these — dead code the sidecar never
   exposes. `web/src/lib/hooks.ts`'s `useUploadBlob`/`useCopyBlob`/`useBlobSasUrl`/`useBlobVersions`/
   `useDeletedBlobs` all call the missing routes. Worse: the Metadata tab's "Save" button
   (`StoragePage.tsx`) has no `onClick` handler at all — it just closes edit mode, so a user believes
   they saved a metadata change and nothing persisted. This is a broader gap than
   `demo-mode-parity`'s Task 4, which only flagged blob-recovery routes — copy/upload/versions/SAS/
   metadata are equally dead. **Recommendation:** treat as its own feature
   (`storage-mutation-endpoints`): wire all five capabilities to their already-implemented
   `IStorageClient` methods, and until that lands, disable (not silently no-op) every affected button
   with a "not yet available" state — silently-broken destructive-adjacent actions are the worst
   failure mode for a storage tool.

5. **Auth secrets (Bearer/Basic/API-key/OAuth2) are now persisted in plaintext inside
   `collections.json`.**
   `RequestEditor.tsx` binds credential inputs straight into the request object, which gets serialized
   whole to disk via `PUT /api/config/collections` → `CollectionRepository.SaveAsync`. In MAUI,
   `credentialKey` was only ever an opaque pointer — the real secret went through
   `ICredentialStore.Save(...)`. Today nothing calls an equivalent save-secret endpoint, so
   `SidecarAuthHeaderBuilder.cs` falls back to treating the credential key itself as the secret. For
   Git-linked collections this means tokens/passwords can end up committed to a repo.
   **Recommendation:** add a persisted secret-store endpoint (OS keychain via Tauri, not the sidecar's
   in-memory dict), have auth inputs save-on-blur and keep only an opaque key in the request tree —
   this is a security fix, not just UX, and should be prioritized above the rest of this doc.

## API Client

6. **Body editor regressed from a real code editor to a plain `<textarea>`** — no syntax
   highlighting, no folding, no inline JSON-validity markers (`RequestEditor.tsx`). *Proposal:*
   CodeMirror6 with JSON/XML modes and inline lint markers — lighter than re-adding Monaco, and
   better than MAUI's footer-only error status since errors show at the offending line.
7. **Capture Rules tab is a non-functional mockup** — local `useState` never initialized from or
   written back to `request.captureRules`; switching requests always shows empty, edits vanish on
   save. Also dropped Header/StatusCode capture sources (JSONPath only). *Proposal:* wire to
   `request.captureRules` properly; add an inline "preview against last response" button per rule —
   an upgrade over MAUI, which had no live preview.
8. **`VariableGeneratorEditor` (dynamic value generation — GUID/timestamp/ranges/Faker/templates)
   has no React equivalent** — `CollectionVariableEditor.tsx` is a flat static grid only. *Proposal:*
   a per-row generator-mode toggle with an inline regenerate/preview chip, folded into the existing
   grid rather than reviving MAUI's separate dropdown-heavy layout.
9. **No per-request quick-jump (old `RequestQuickNavPanel`, Ctrl+K across all requests)** — the
   global `CommandPalette.tsx` only has a static link to `/api-client`. *Proposal:* feed open
   collections/requests into the existing command palette as dynamic entries instead of building a
   second overlay (see Shell finding #16 — same underlying palette upgrade serves both).
10. **Multi-repo Git management collapsed to one hardcoded repo** (`ApiClientPage.tsx` renders
    `<GitPanel repoPath="." />` unconditionally) — no add/list/remove linked-root UI that
    `ApiClientManagementScreens.razor` had. *Proposal:* a lightweight repo-picker dropdown above
    `GitPanel`, sourced from collections' linked-root metadata, if multi-root is still wanted.
11. **No conflict-resolution UI for linked (Git) collections edited externally** — MAUI's
    reload/keep-mine/save-as-copy banner has no equivalent. *Proposal:* a dismissable toast with the
    three actions, triggered off a 409/stale-stamp save response.

## Redis

12. **`RedisKeyspaceHealthExplorer`'s actual analysis (no-TTL, oversized-value, heavy-prefix,
    hot-key detection) was dropped** — the React "Keyspace Health" tab just duplicates the Server
    Info tab's stats; `RedisKeyspaceHealthAnalyzer` (a pure, portable C# algorithm) has zero callers
    anywhere in `src-sidecar`/`web/src`. This is the single biggest loss for the target user
    (developer hunting "why is this cache blowing up"). *Proposal:* expose
    `POST /api/redis/{cacheId}/health/analyze`, render as a sortable/filterable findings table with
    severity chips and click-to-open-key.
13. **`RedisPrefixMemory` shows key *counts*, not memory bytes**, despite the panel being titled
    "Prefix Memory Breakdown" — never calls `MEMORY USAGE`. *Proposal:* port the batched
    `MEMORY USAGE` sampler MAUI had and render a stacked/treemap-style bar instead of a flat table.
14. **Hash/List/Set/Zset detail views are fully read-only** — no field/score add/edit/delete, even
    though the underlying `IRedisClient` methods exist and just aren't exposed by
    `RedisEndpoints.cs`. A routine "patch one field in prod" action now requires dropping to
    `redis-cli`. *Proposal:* add the missing mutation routes and inline edit affordances, matching
    what string values already get.
15. **Namespace tree lost recursion, configurable separator, and virtualization** — only splits on
    the first `:`, hard-caps children to 20 with a "+N more" label instead of lazy-loading, and the
    flat key list isn't virtualized (will jank at a few thousand keys). *Proposal:* a real recursive
    tree with a user-configurable separator and lazy-loaded children, `@tanstack/react-virtual` for
    both the tree and flat list.

## Storage

(Beyond the critical wiring gaps in finding #4:)

16. **Upload is text-paste only — no real file picker/drag-drop, no progress, no overwrite guard.**
    *Proposal:* `react-dropzone` onto the blob list pane, `multipart/form-data` streaming to a new
    upload route, per-file progress via `XHR upload.onprogress`, overwrite-confirm step.
17. **`BlobVersionDiffPane` has no UI at all** — versions list with no compare action, despite
    `GetVersionComparisonAsync` existing with zero callers. This is arguably a recovery tool's most
    useful feature. *Proposal:* selectable version rows (max 2) + "Compare": metadata diff table plus
    a real line-level text diff for text blobs (color-coded unified or side-by-side).
18. **`allowMutations` safety gate is configured in Settings but never enforced in the UI** —
    Upload/Copy render unconditionally regardless of the per-account read-only flag. *Proposal:* a
    persistent status strip under the Storage header (gray "Read-only" / amber "Mutations enabled —
    writes are permanent"), and actually `disable` the buttons when read-only, not just decorate.
19. **Copy dialog dropped destination-container autocomplete and the overwrite warning** — free-text
    input instead of a picker sourced from known containers. *Proposal:* combobox from
    `useStorageContainers`, restore the overwrite checkbox + warning.

## Dashboard

20. **No customization/builder exists in React at all** — a single hardcoded page; MAUI's
    tile-builder (add/remove/reorder/resize, saved views, custom watch tiles) has no equivalent, and
    the `dashboard-redesign` plan treats the builder as something to *reorganize*, not something
    that needs to be built from scratch for React. *Proposal:* skip re-porting the old builder
    mechanics; add a "Pin to dashboard" affordance directly on resource rows across AKS/Service
    Bus/Redis/Storage, backed by a simple ordered list in profile storage, rendered as a "Pinned"
    section on the dashboard — most of the value, far less UI surface.
21. **`ConfigurationReadinessDashboard` (first-run setup guidance) has no home anywhere** — the
    redesign plan explicitly excludes it from Dashboard scope, but Settings has no readiness summary
    either. *Proposal:* a "Getting started" checklist in Settings' General tab — one row per area
    (Service Bus/AKS/Redis/Storage/Agent) with a status pill and click-through, useful as a
    standing checklist, not just a first-run gate.

## Shell / Layout

22. **Status bar lost almost all signal** — one global sidecar dot + version + demo flag, replacing
    MAUI's per-area (SB/AKS/Redis/Storage) connection dots with tooltips, last-refresh timestamps,
    port-forward count, and background-task progress. *Proposal:* a live per-area health strip
    sourced from data each page already fetches (no new bespoke event bus needed), plus a
    background-task indicator fed by React Query's in-flight mutation count.
23. **Command palette regressed to a static 9-item substring filter** — no fuzzy scoring, no
    "Recent"/"Favorites", no resource-level search (queue/namespace/pod names), unlike MAUI's fuzzy,
    categorized, MRU-aware palette. *Proposal:* add a small fuzzy scorer, an MRU list in local
    storage, and let feature pages register searchable resource entries into a shared palette
    registry — this single upgrade also solves API Client finding #9.

## Settings

24. **`DevOpsSettings.tsx` is dead UI that contradicts the permanent-drop decision** — still a live
    tab in `SettingsPage.tsx`, Save has no handler, no sidecar route or hook backs it. Actively
    misleading given DevOps was permanently dropped on 2026-07-26. *Proposal:* delete the tab and
    file, per `demo-mode-parity`'s own cleanup task — this is pure follow-through, not a new
    decision.
25. **Appearance settings has two dead controls** — Font Size and Density selects have no
    `value`/`onChange`, so changing them does nothing. *Proposal:* wire to the settings store and
    apply via CSS custom properties, or remove until implemented.

## AKS / Service Bus — UX quality beyond the existing bug-fix plans

26. **No resizable detail panels anywhere** — every side panel (pod detail, YAML, Helm, secrets,
    Service Bus message list/detail) is hardcoded to a fixed width; MAUI's `ResizablePanel.razor`
    gave every one a drag handle. *Proposal:* a shared `<ResizablePanel>` persisting width per
    panel-kind in local storage — can also add double-click-to-maximize, which MAUI didn't have.
27. **Destructive-action mutations have no error feedback** — `useAksRestartDeployment`,
    `useAksScaleDeployment`, `useAksDeletePot` (and likely their Service Bus equivalents) define only
    `onSuccess`; a failing call does nothing visible even though `NotificationSystem.tsx` already
    exists as a toast surface. *Proposal:* a generic `onError` in shared mutation options that toasts
    the server's error message.
28. **Live CPU/Memory bars are gone from the Pods grid** — `PodsTab.tsx` shows only
    Name/Status/Ready/Restarts/Node/Age; MAUI's `PodGrid.razor` had inline resource-usage bars. A
    real at-a-glance debugging capability lost. *Proposal:* restore a resource-usage column with the
    same metrics-server-unavailable fallback MAUI had.
29. **No bulk/multi-select actions in the Service Bus message list** (MAUI didn't have this either —
    flagged as a genuine opportunity, not a regression). *Proposal:* batch-complete/batch-resubmit
    selected DLQ messages — also reduces the appeal of the currently-broken net-new Batch Replay
    feature already flagged in `service-bus-migration-fixes`.
30. **No browser-style drill-down history** — selected pod/YAML resource/SB entity live in plain
    `useState`, not the URL, so back/forward, refresh, and deep-linking all lose state. *Proposal:*
    promote namespace/active-tab/selected-resource to route/query params — a concrete win specific
    to being a web app, which MAUI/WebView couldn't offer.

## Dependencies

- Finding #5 (secret storage) should land before or alongside anything else touching
  `RequestEditor.tsx` — don't build more auth UI on top of the plaintext-storage bug.
- Findings #1, #2, #4 (Monitoring, Pub/Sub, Storage mutations) each look like sizeable standalone
  features once scoped — recommend splitting into their own `docs/features/active/` folders before
  implementation starts, rather than folding into this review doc.
- Independent of [tauri-security-hardening](../tauri-security-hardening/index.md), but any new
  sidecar routes proposed here should follow whatever CORS/auth pattern that plan lands on.

## Risks

| Risk | Mitigation |
|---|---|
| This doc is reviewed as one giant backlog and nothing gets prioritized | Treat the 5 "critical" findings (misleading UI) as the actual next action; everything else can be triaged independently |
| Monitoring/Storage-mutations/Redis-health turn into large scope creep if bundled with quick fixes | Split each into its own feature folder with its own index/status before starting implementation |
| Findings here overlap with work the other in-progress session is doing on `aks-migration-fixes`/`service-bus-migration-fixes` | This review was deliberately scoped to exclude anything already in those docs — re-check before starting in case they've since expanded |
