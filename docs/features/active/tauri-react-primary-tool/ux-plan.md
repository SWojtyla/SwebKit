# UX / UI / Functionality Plan

Companion to [production-readiness-review.md](production-readiness-review.md). This plan is phased
by risk/impact and dependency — do not reorder phases without checking dependencies noted per task.
Each task lists the files most likely involved (verify against current code before starting; some
line numbers will drift as earlier phases land).

---

## Phase 0 — Critical bug fixes (do first)

These are correctness bugs found in the running app, independent of any other phase, and cheap
relative to their impact.

### 0.1 Trace and fix the React key-collision duplicate-render bug

**Why first:** confirmed via live console error (`Encountered two children with the same key`,
`Date.now()`-shaped key), reproduced in three unrelated features. Highest-priority item in the whole
review because it may not be UI-only.

- Grep the codebase for `key={Date.now()`, `key={.*\.now\(\)}`, or any list `.map(...)` whose `key`
  prop is derived from a timestamp rather than a stable ID (`id`, `messageId`, `ruleId`, etc.).
  Prime suspects, in order of where the bug was observed: the API Client error/notification list,
  `MonitoringPage.tsx`'s alert-history row list, and whatever renders AKS action-confirmation
  notifications (`NotificationSystem.tsx` / `useNotifyMutation.ts`).
- For each site found, replace the key with a stable unique identifier. If the underlying data
  genuinely has no stable ID (e.g., a client-generated notification), generate one **once** at
  creation time (`crypto.randomUUID()`) and store it on the object, not re-derive it at render time.
- **Critical follow-up, do not skip:** for each of the three reproduction sites, verify whether the
  *action itself* fires twice (e.g., does an AKS restart actually call the sidecar's restart
  endpoint twice, or is only the toast/notification duplicated?). Check the relevant `useMutation`
  call and its `onSuccess`/`onSettled` handlers for double-invocation, not just the rendered list.
  If a real mutation is double-firing, this becomes a data-safety bug (e.g., a message could be
  resubmitted twice), not just a cosmetic one.
- Add a regression e2e assertion (Playwright) that triggers one of the three flows (e.g., AKS
  restart) and asserts exactly one notification/toast/row appears, not "at least one."

### 0.2 Fix invalid nested `<button>` in Service Bus entity tree

- File: likely `web/src/components/service-bus/EntityTree.tsx` or wherever `entity-tree-queue-*`
  rows are rendered.
- The row is currently a `<button>` containing the active/DLQ count badges as nested `<button>`s.
  Change the row to a non-interactive container (`<div role="button" tabIndex={0}>` or a `<li>`)
  with its own click/keyboard handler, and keep the count badges as real `<button>`s (or plain
  `<span>`s with their own click handler stopping propagation) as siblings, not descendants.
- Verify no other tree/list component in the app has the same pattern (grep for `<button` inside
  another `<button`'s JSX block across `web/src/components`).

### 0.3 Fix API Client demo request pointing at the wrong port

- The bundled "Health Check" demo request targets `http://127.0.0.1:5198/health`; the sidecar's real
  default dev port is `5199` (per `web/src/lib/api.ts`'s documented default). Find where this demo
  request is seeded (likely `src-sidecar/Endpoints/DemoModeService.cs` or a demo-collection seed
  file) and fix the port, or — better — make it relative/environment-derived so it can't drift again
  (e.g. reference `{{baseUrl}}/health` with an environment variable set from the actual resolved
  sidecar port, if the demo environment supports variable substitution).

### 0.4 Clean up API Client demo-mode data pollution

- Demo Mode currently renders whatever is in the real persisted `collections.json`/`environments.json`
  store, which has accumulated ~24 leftover dev/e2e-test artifacts ("Test Environment," "Tab Test
  Collection," "E2E Collection," etc.) alongside the intended demo content (JSONPlaceholder/HTTPBin/
  GitHub API collections).
- Two-part fix:
  1. One-time cleanup: purge the non-demo test artifacts from whatever persisted store ships/is
     seeded with the app (check `.e2e-appdata`-style seeding vs. a real default profile).
  2. Structural fix: make API Client Demo Mode behave like every other feature area's demo mode —
     inject isolated, ephemeral demo collections/environments rather than rendering the real
     persisted store. Check how `DemoModeService.cs` does this for Service Bus/Redis/Storage/AKS
     and apply the same pattern to the API Client's collection/environment repositories.
- Regardless of the above, add search/filter to the Environment Manager and Collection picker so
  this class of clutter is survivable even if it recurs (the collection tree already has a search
  box — the environment selector doesn't).

### 0.5 Fix AKS "Not configured" vs. "Connected" inconsistency in demo mode

- Currently: Dashboard, Settings' readiness checklist, and the footer status bar all show AKS as
  "Not configured" in demo mode, while the AKS page itself works fully with rich demo data. Settings'
  AKS tab has a banner explaining this ("connection fields are inert in Demo Mode") but nothing
  fixes the other three surfaces.
- Fix: when Demo Mode is on, every "is this configured?" surface (Dashboard health tile, Settings
  readiness checklist, footer status dot) should read the same "demo-configured" state AKS's own
  page already derives, rather than checking the (deliberately inert) real connection config. Find
  the shared `isConfigured`/`isReady` check each surface uses and make it demo-mode-aware
  consistently, matching how Service Bus/Redis/Storage already report "Ready" in demo mode.

### 0.6 Fix API Client narrow-viewport response-panel clipping

- At ≤700px viewport width, the response panel's content (972px) overflows its container (476px)
  with `overflow: hidden` and no scrollbar — ~496px is inaccessible.
- File: the API Client's resizable 3-pane layout (`web/src/components/ui/ResizablePanel(s).tsx` +
  wherever panel minimums are configured for the API Client, per the pitfall already documented in
  `docs/pitfalls/react-frontend.md` about fixed-pixel panel minimums not summing to fit a narrow
  window). Add either a responsive minimum-width breakpoint that stacks panels vertically below a
  threshold, or ensure the panel's own content scrolls (`overflow-x: auto`) independent of the panel
  container.
- Add a Playwright test at a narrow viewport (e.g. 700px) asserting the response body is fully
  reachable (via scroll or stacked layout), extending the existing `api-client-layout.spec.ts`
  narrow-width test to also check the response pane specifically.

### 0.7 Fix AKS Pod YAML viewer showing Deployment-shaped content

- The demo data generator returns Deployment-shaped YAML (`replicas`, `selector.matchLabels`,
  `template:`) and lowercase `kind: pod` when a Pod's YAML is requested.
- File: wherever demo YAML is generated for AKS resources (`src-sidecar/Endpoints/DemoModeService.cs`
  or a dedicated demo-data factory) — add a real Pod-shaped YAML template (`kind: Pod`, `spec.containers`,
  `status.phase`, etc.) distinct from the Deployment template.

### 0.8 Delete the dead `DevOpsSettings` tab

- DevOps/Pipelines was permanently dropped per the 2026-07-26 decision. `SettingsPage.tsx` still
  renders a `DevOpsSettings.tsx` tab with no working Save handler and no backing sidecar route.
- Remove the tab, the component, its route/nav entry, and any lingering `devopsConfig`
  read/write path in `lib/hooks.ts`/`lib/api.ts` that only existed to serve it. Check
  `docs/architecture/functionalities/` for a stray DevOps doc to retire alongside it.

---

## Phase 1 — Land what's already built (ABANDONED — branch no longer exists)

**Status update (2026-08-01): dropped.** This phase assumed `monitoring-rebuild`,
`api-client-git-completion`, and `api-client-ux-overhaul` were functionally complete and just sitting
uncommitted on `feat/api-client-ux-and-git`. That branch does not exist anywhere reachable in this
repo (confirmed absent both locally and on origin across multiple passes), and the user has confirmed
it no longer exists — whatever work it held is gone, not merely unlanded. Nothing from this phase is
being pursued; any future work in this area (e.g. `ApiClientPage.tsx`/`RequestEditor.tsx`
decomposition in `technical-plan.md` §2.3, which explicitly deferred to "after Phase 1 lands") should
now be scoped fresh against the current `ApiClientPage.tsx`/`RequestEditor.tsx`, not treated as
blocked on this phase. The three numbered items below are kept for historical record only.

<details>
<summary>Original plan (no longer actionable)</summary>

The single fastest way to make real progress: three features are functionally complete per their own
status docs but sitting uncommitted.

1. **`monitoring-rebuild`** — status `Done`, 7/7 xUnit tests passing, smoke-tested. Commit as-is
   after a final `dotnet test`/`npm run test:unit`/`npx playwright test` pass to confirm nothing
   drifted since it was marked done.
2. **`api-client-git-completion`** — status `Review`, implemented on `feat/api-client-ux-and-git`,
   not committed. Its own status doc lists manual desktop-shell verification steps 1–9 as not yet
   performed (Tauri IPC round-trip, `restore_allowed_root` across a real app restart, push/pull
   against a real remote, drawer rendering under the desktop shell). Perform those steps, fix
   anything they surface, then commit.
3. **`api-client-ux-overhaul`** — status `Review`, same uncommitted branch, same "manual desktop
   verification not performed" gap. Also note its own doc flags `ApiClientPage.tsx`'s bundle chunk
   has grown to ~476kB, approaching Vite's 500kB warning threshold — worth a quick check whether
   this phase's own changes pushed it over, and if so scope a lazy-load split as part of landing it
   rather than deferring.

Do the manual desktop-shell verification for #2 and #3 together (both live on the same branch) —
launch the actual Tauri app (not just the Vite dev server) and walk through the numbered steps in
each feature's `status.md` before committing.

</details>

---

## Phase 2 — Feature-parity gaps rated "Important"

These are capabilities MAUI has that Tauri+React doesn't, rated important enough to block calling
the new app a full replacement — but confirmed **not architecturally hard**, just not built yet.

### 2.1 Agent tool-calling

- MAUI's `SwebKit.Agents` already has a working `IAgentToolRegistry`, `AgentActionApplier`,
  `AgentCapabilityTester`, and 9 concrete `IAgentTool` implementations (5 AKS, 2 Service Bus,
  2 Observability — the 2 Observability ones are moot per the dropped-feature decision, so 7 are
  relevant). All of them call `IAksClient`/`IServiceBusClient`, both already fully wired into the
  sidecar.
- Work:
  1. Register the 7 relevant tool implementations for sidecar use (wire them the same way
     `SidecarMonitoringConnectionPool` already provides AKS/SB clients to other sidecar consumers).
  2. Remove the hard-coded *"No tool calling is available in the sidecar mode"* /
     *"You do not have access to live data tools"* strings from `SidecarAgentChatService.cs` and
     wire `AgentModelRequest.Tools` from the registered tool set instead.
  3. Add a tool-execution status affordance to `AgentPage.tsx` (MAUI's `ToolExecutionStatus.razor`
     is the reference — doesn't need to be a port, just "tool X is running / ran, here's the
     result" feedback so the chat isn't a silent black box when a tool call happens).
  4. Update `docs/architecture/functionalities/agent.md`, which still documents the MAUI-only
     architecture as current.
- Test: at least one e2e test per tool category (an AKS-data question, a Service-Bus-data question)
  asserting the agent's response reflects live demo data, not just a generic LLM answer.

### 2.2 Pod shell (interactive `kubectl exec`)

- MAUI's `KubectlShellLauncher.Launch` just shells out to `kubectl exec` in an external terminal
  process (`UseShellExecute=true`) — the same class of subprocess management `src-tauri/src/git.rs`
  and `native.rs` already do for other commands.
- Work: add a Tauri command (in `native.rs` or a new module) that shells out to `kubectl exec -it
  <pod> -- sh` (or an equivalent interactive shell) in a new terminal window, or — if an in-app
  terminal is preferred over spawning an external window — wire it into the existing xterm.js-based
  terminal component referenced in earlier architecture notes. Remove the hard-coded
  `disabled: true` on `PodsTab.tsx`'s "Open shell in pod" context-menu item once wired.

### 2.3 Real port-forward subprocess management

- `native.rs`'s port-forward commands currently only maintain a session registry — no real
  `kubectl port-forward` subprocess is spawned (comment in the code admits this).
- Work: implement the actual subprocess spawn/monitor/kill lifecycle for `start_port_forward`/
  `stop_port_forward`, following the same process-management pattern already proven for the sidecar
  itself in `src-tauri/src/sidecar.rs` (spawn, capture stdout for readiness/error detection, clean
  kill on stop/app-exit). Surface real connection status (not just "session registered") in
  `PortForwardPanel.tsx`.

### 2.4 Storage bulk download as a real ZIP

- `StoragePage.tsx`'s "batch download" currently loops `handleDownloadBlob` sequentially — N
  separate file downloads, not a bundle. Service Bus already proved the pattern
  (`buildZip` in `MessageList.tsx`, real ZIP with progress) in this exact stack.
- Work: port the same `buildZip`-based approach to Storage's batch download, with the same
  progress-reporting UX Service Bus already has.

---

## Phase 3 — Finish already-tracked active features

These 7 features are already scoped in their own `docs/features/active/*` folders; this plan does
not re-specify them, just tracks them to closure as part of the primary-tool push. Check each
folder's own `technical-plan.md`/`test-plan.md` for the authoritative task list; only the current
blocking/remaining item is noted here.

| Feature | Status | What's left |
|---|---|---|
| `aks-ux-improvements` | Planned | Not started: sidecar YAML-apply endpoint hardening, `NamespaceSelector` reorder-selected-first, global loading indicator on context/namespace switch |
| `service-bus-download-parity` | Planned | Not started: message download (single + bulk ZIP — note ZIP pattern now also needed for Storage per 2.4 above, consider building the shared helper once), accurate Active/DLQ counts, infinite-scroll pagination |
| `react-polish-aug-01` | Planned | Not started: Redis namespace-tree restore (partially superseded by `ux-followup-july-27` — reconcile which is still needed), Storage 404 on slash-containing blob names, missing success/error toasts on Settings export/AKS actions, AKS query-key invalidation bug, AKS namespace-fanout perf |
| `storage-detail-overflow` | In Progress | Small scoped CSS fix (`break-all`, `table-fixed`, `whitespace-pre-wrap`) in one file — near-trivial, finish it first among this group |
| `ux-followup-july-27` | Review | Implementation appears done per its plan; needs a verification pass (its own test-plan.md) before being marked closed |
| `post-migration-ux-review` | In Review (triaged) | Re-triage against current code: several of its "critical" findings are already fixed (see review §1.10) — confirm the remainder (Monitoring being a full mockup is the one still-critical, still-open item; everything else in its finding list should be re-checked against the corrections in [production-readiness-review.md](production-readiness-review.md) §1 and §4 before scheduling) |
| `aks-internal-refactor` | Done (test-verified) | One known bug remains: demo sidecar's `DemoAksClient.ScaleDeploymentAsync` doesn't persist the new replica count so the table doesn't update after a Scale action — fix before closing |

---

## Phase 4 — Accessibility pass

Treat as a deliberate scope decision now that this becomes the daily-driver tool, not an afterthought.

1. **Modal/overlay roles**: add `role="dialog"` + `aria-modal="true"` (+ `aria-labelledby` where a
   title exists) to the command palette, Service Bus entity-search palette, keyboard shortcuts
   panel, and every other modal/dialog surface (Alert Rule Dialog, Collection Export Dialog, Storage
   Copy dialog, confirm dialogs). Only one `role="dialog"` exists app-wide today (`GitDrawer.tsx`).
2. **Custom tree/list widgets need real ARIA roles**: `CollectionTree` (API Client), `EntityTree`
   (Service Bus), Redis's key tree, and AKS's `ResourceTable` are all built from plain `div`s. Add
   `role="tree"`/`role="treeitem"` (or `role="listbox"`/`role="option"` where a flat list is more
   accurate than a tree) plus keyboard navigation (arrow keys, Home/End) and `tabIndex`/focus
   management, starting with Redis's key tree (currently fully keyboard-unreachable — confirmed bug,
   see review §3 finding #9).
3. **Contrast fixes**: light-theme AKS pod status text (measured 2.09:1 green / 3.10:1 red against a
   4.5:1 AA requirement) — adjust the token values in the Aurora theme's light-mode CSS custom
   properties, not per-component overrides, so the fix is systemic.
4. **Add an automated accessibility test tier** (see [test-plan.md](test-plan.md) — `axe-core`
   integration) so regressions here are caught going forward rather than requiring another manual
   pass.

---

## Phase 5 — Visual consistency & polish

1. **Storage icon consistency**: replace emoji file/folder icons (📄/📁) with the lucide-react icon
   set used everywhere else in the app (`File`/`Folder` icons, matching AKS/Service Bus usage).
2. **Date format consistency**: pick one convention (ISO `YYYY-MM-DD` is already used in AKS pod
   logs/CSV content and is the more debugging-tool-appropriate choice) and apply it everywhere —
   Storage and Monitoring currently use `DD/MM/YYYY`. Centralize into a single `formatDate` helper in
   `lib/` if one doesn't already exist, and use it everywhere a date is rendered.
3. **Redis/Slow-Log duration formatting**: replace raw `TimeSpan.ToString()` output
   (`00:00:00.0483000`) with a human-readable formatter (`48.3ms`, `1.2s`) — likely a small pure
   function in `lib/redis-format.ts`, which already exists and is the natural home for it.
4. **AKS tab discoverability**: reconsider promoting "Services" to a top-level tab (it's arguably the
   most commonly-checked resource) rather than nested inside the secondary "Network" dropdown
   alongside Ingresses/GatewayClasses/Gateways/HTTPRoutes.
5. **Dashboard copy bugs**: "1 caches"/"1 accounts" → singular when count is 1 (simple pluralization
   helper, likely already exists elsewhere in the codebase for similar cases — reuse it).
6. **Extract shared `EmptyState`/`Skeleton`/`Dialog` components** (also a technical-plan item, listed
   here because it's the direct fix for the inconsistent, hand-rolled loading/error/empty markup
   observed across Redis/Storage/AKS/Monitoring during the hands-on review) — see
   [technical-plan.md](technical-plan.md) §"Shared UI primitives" for the implementation task; this
   UX plan is the consumer of that work, not its owner.

---

## Phase 6 — Nice-to-have UX enhancements

Lower priority; schedule after Phases 0–5, or opportunistically alongside related technical-plan
refactors.

- **URL-driven drill-down for Redis/Storage/Monitoring** (AKS and Service Bus already have this —
  namespace/tab/selected-resource in the route, back/forward/deep-link support). Extend the same
  pattern used in `AksWorkspaceContext.tsx` to the other feature areas.
- **Resource-aware command palette**: extend `CommandPalette.tsx` beyond the 9 static nav entries to
  include live resource names (queue names, pod names, namespace names) — the fuzzy-search/MRU
  infrastructure is already there per the parity research; this is a data-source extension, not a
  new component.
- **Command-palette registry consolidation**: merge `CommandPalette.tsx` and
  `service-bus/EntityCommandPalette.tsx` onto one shared command registry, so future per-feature
  command-palette entries don't require a third bespoke implementation.
- **Per-area live status dots** in the footer status bar (SB/AKS/Redis/Storage individually, not
  just one global dot) — MAUI's `StatusBar.razor` is the reference.
- **Bulk/multi-select DLQ actions in Service Bus** (batch complete/resubmit) — genuinely new
  capability neither app has had; low priority but a real productivity win once the rest of Service
  Bus's parity work (Phase 3 above) lands.
- **Network Policy / PDB / Placement Constraints panels in AKS** — MAUI reference components exist
  (`NetworkPolicyAnalysisPanel`, `PodDisruptionBudgetPanel`, `PlacementConstraintsPanel`); no sidecar
  endpoints exist yet for any of the three.
- **GraphQL subscriptions UI** in the API Client (backend service already exists, just needs a
  frontend panel — same shape as the existing WebSocket panel).
