# Codebase Quality Program — megaplan

`State: In Progress`

## Goal

Deep-scan the entire live codebase (legacy MAUI excluded — deleted) for code quality,
refactoring needs, best practices, performance, bugs, and dead code. Executed in phases,
one PR per phase. Ends with a redesigned Dashboard ("AI cockpit").

## Codebase scale (recon, 2026-09-24)

| Area | Source LOC | Notes |
| ---- | ---------- | ----- |
| `web/src` | ~62k | aks 8.8k, api-client 8.6k, service-bus 6.8k, settings 5k, storage 3.6k, redis 2.7k, monitoring 2.4k, agent 2.4k, sql 2k, layout 1.4k, dashboard 0.8k, ui 0.8k |
| `src-sidecar` | ~10k | minimal-API endpoints |
| shared libs | ~48k | Core 28.6k (incl. demo clients), K8s 7.5k, Agents 7.2k, Azure 2.4k, DevOps 1.2k, Redis 1k, Sql 0.8k |
| `src-tauri/src` | ~2.9k | Rust shell |
| `tests/` + `web/e2e` | ~66k | |

Seed findings found during recon:

- 28 committed build artifacts under `src/*/artifacts/copilot-build/` (DLLs, PDBs, deps.json)
- ~560 legacy files: `src/SwebKit.App`, `src/SwebKit.WinUI`, `src/SwebKit.Agent.PocConsole`,
  `tests/SwebKit.App.Tests`, `scripts/maui/`
- Hotspots: `DemoAksClient.cs` 2.9k, `KubernetesAksClient*.cs` ~5k across partials,
  `MessageList.tsx` 1.6k, `types.ts` 1.5k, `StoragePageContext.tsx` 1.2k,
  `ApiClientPageContext.tsx` 1.1k, `AksWorkspaceContext.tsx` 1k — the `*PageContext.tsx`
  god-context pattern is a recurring smell
- Bundle: `mermaid.core` 623kB, `cytoscape` 443kB, `AksPage` 485kB, `codemirror-theme`
  367kB — no manualChunks strategy
- `SwebKit.DevOps` (1.2k) may be near-dead — sidecar csproj comment says DevOps was
  "fully dropped"; verify residual usage
- `AppInsightsDiscoveryService` exists but is unwired (no DI registration, no endpoint)
  — known gap from `docs/reviews/production-readiness-2026-08-06.md`

## Decisions

- Phase order: **Hygiene → Architecture → Features → Dashboard**
- Delete all legacy MAUI code (git history preserves it)
- Per phase: **fix small issues inline, flag structural/risky changes for approval**
- Dashboard: **full AI cockpit** per `docs/reviews/production-readiness-2026-08-06.md`
- The megaplan (this file) is the living doc: each phase appends to the Findings Log;
  big sub-efforts (Dashboard) may spawn their own plan file

## Workflow per phase

1. Scan the phase's scope thoroughly (all files, not just hotspots)
2. Append findings to the Findings Log below
3. Apply mechanical/small fixes in the phase branch
4. Flag structural changes with a short proposal before touching code
5. Verify: `tsc -b`, eslint, `vitest`, `npm run build`, `dotnet build` + `dotnet test`
   (affected projects), full `npx playwright test`, `cargo clippy`/`cargo test` if
   `src-tauri` touched
6. Open PR per phase; merge before starting the next

## Tasks

### Phase 0 — Repo hygiene & dead code

- [x] Delete `src/*/artifacts/copilot-build/**` (28 committed binaries; `.gitignore`
  already covered `artifacts/` — files predated the rule)
- [x] Delete `src/SwebKit.App/`, `src/SwebKit.WinUI/`, `src/SwebKit.Agent.PocConsole/`,
  `tests/SwebKit.App.Tests/`, `scripts/maui/`, plus `tests/SwebKit.E2E.Tests/`
  (found during scan: it drove the MAUI app via WebView2 CDP — dead with MAUI gone)
- [x] Remove MAUI job + `maui` filter from `.github/workflows/build.yml`;
  **fixed a real CI gap**: `src/SwebKit.Sql/**` and `tests/SwebKit.Sql.Tests/**` were in
  no filter — a Sql-only change would have skipped every job. Added to `shared_dotnet`,
  Sql build+test added to the core job.
- [x] `SwebKit.slnx`: removed 3 deleted projects; **found it was also missing**
  `src-sidecar`, `SwebKit.Sidecar.Tests`, `SwebKit.Agents.Tests` — added, so
  solution-level `dotnet build`/`dotnet test` now covers everything.
- [x] Delete `docs/packaging-and-install.md`, `docs/pitfalls/blazor-maui.md`; updated
  `scripts/README.md`, `README.md`, `CLAUDE.md`, `docs/context.md`, pitfalls index +
  see-also footers, `.gitignore`, `.editorconfig`, `useLogBuffer.ts` comment
- [x] Dependency audit: removed 11 unused `PackageVersion` entries
  (Maui.Controls, WebView.Maui, Blazor-ApexCharts, BlazorMonaco, FluentUI ×2, Markdig,
  bunit.web, Microsoft.Playwright, Logging.Debug, DependencyInjection) and 2 unused
  npm deps (`@tauri-apps/plugin-dialog`, `plugin-clipboard-manager` — JS bindings
  unused; Rust-side commands still registered)
- [x] Closed out shipped feature plans (`aks-storage-ux-improvements.md`,
  `ai-insight-reports/`, `aks-multi-context-alerting/`) + catalog

### Phase 1 — Global architecture

- [x] **`docs/architecture/` rewrite** — `architecture.md`, `codebase-guide.md`,
  `design.md`, `index.md` rewritten against `web/` + `src-tauri/` + `src-sidecar/`.
  `functionalities/releases.md` + `incident-timeline.md` deleted with the island;
  dead-feature references surgically stripped from the other `functionalities/*.md`
  (full per-feature rewrites still scheduled during their Phase 2 deep dives —
  several remain MAUI-era in detail).

- [x] Sidecar: `Program.cs` DI audit — clean: grouped registrations, singleton
  connection pools per feature, secret-safe global exception handler, per-feature
  `Map*Endpoints`. Fixed stale `MauiProgram.cs` comments; moved `DemoModeService`
  `Endpoints/` → `Services/` (namespace now `SwebKit.Sidecar.Services`).
- [x] Shared libs: `SwebKit.DevOps` island + ~6k LOC of unwired Core services/
  abstractions/models deleted (approved, see Findings). `Core/Services/` still
  mixes demo clients with live services — split proposal deferred to Phase 2.
- [x] Web: god-contexts + `types.ts` + `api.ts` audited — findings logged, splits
  flagged for Phase 2 (per-feature). Mixed state model (Zustand stores + 4 page
  contexts + React Query) noted as convention drift, not a bug.
- [x] Bundle/perf: routes lazy, `manualChunks` splits react/query/icons, heavy libs
  (mermaid/cytoscape/codemirror) dynamic-imported — bundle healthy, no action.
- [x] Rust shell: `unwrap()` audit — production `unwrap`/`expect` sites in
  `native.rs`/`pod_shell.rs`/`sidecar.rs` are mutex/child-process idioms
  (acceptable); `git.rs` unwraps are test-only. `git.rs` 1.3k split → Phase 2.

### Phase 2 — Feature deep dives (biggest first)

- [ ] `aks` (8.8k) → `api-client` (8.6k) → `service-bus` (6.8k) → `settings` (5k) →
  `storage` (3.6k) → `redis` (2.7k) → `monitoring` (2.4k) → `agent` (2.4k) → `sql` (2k) →
  `layout`+`ui` (2.2k combined)
- Per feature: all components + hooks + matching sidecar endpoints + lib code; clean
  code, duplicated logic, perf (memoization, virtualized lists, query keys), bugs, dead
  code, a11y, testid conventions (`swebkit-ui-ux-guardrails`), e2e coverage gaps

### Phase 3 — Dashboard AI cockpit (design-gated)

- [x] Design proposal presented for approval before implementation — cockpit
  ingredients from the prior review: proactive-insights feed with Investigate,
  workspace topology graph (cytoscape), pinned agent conversation, NL command bar;
  keep health tiles/pins. Approved direction: **full AI cockpit**.
- [x] Implemented: `DashboardPage.tsx` (799 LOC monolith) split into focused
  components — `CockpitCommandBar`, `AgentStatusStrip`, `ServiceGrid`,
  `WatchTiles`, `CockpitTopology`, `InsightsFeed`, `PinnedShortcuts`, plus the
  `useServiceHealth` aggregator.
- [x] Command bar queues a prompt via `useAgentPanelStore.queuePrompt` and docks
  the global agent panel open — the panel (single `useAgentChatStream` owner)
  sends it, instead of navigating to `/agent` and injecting a bare user message.
- [x] Consolidated the triplicated service presentation (health tiles + resource
  rows + tool cards) into one `ServiceGrid`: per-service card with live
  connectivity, **every** configured entity with its own status dot (no more
  silent `[0]` sampling), and an inline pin button.
- [x] Real Investigate: deep-links to `/monitoring?tab=reports&report=<id>`
  (deterministic `proactive-{ruleId}-{firedAtMs}` id) where the genuine
  report → chat handoff lives; the fabricated fake-assistant-reply injection is
  gone.
- [x] Workspace maps render as the real `TopologyGraph` (cytoscape) with
  per-area colors, map picker, and node-click → feature-page navigation.
- [x] Pins relabeled "Pinned shortcuts"; pending-approvals banner now docks the
  agent panel instead of navigating away; agent peek shows the latest assistant
  reply inline.
- [x] New `lib/stores/agent-panel.ts` store (open/queuePrompt) shared between
  AppLayout and the dashboard.

## Test plan

- Per phase: the CI-equivalent local sweep (typecheck, lint, unit, build, sidecar
  build+test, full Playwright, cargo checks if rust touched)
- Phase 0 additionally: `git grep` for references to deleted paths; confirm CI runs
  without the maui job

## Validation results

- **Phase 0** (2026-09-24): `dotnet build SwebKit.slnx` clean (0 warn/0 err);
  `dotnet test` 2260/2260 (Core 1074, Sidecar 542, Agents 258, K8s 159, Azure 152,
  Sql 46, DevOps 29); vitest 534/534; `vite build` clean; Playwright 375/375
- **Phase 1** (2026-09-25): `dotnet build SwebKit.slnx` clean (0 warn/0 err);
  `dotnet test` 1941/1941 (Core 786, Sidecar 542, Agents 258, K8s 158, Azure 151,
  Sql 46 — lower totals reflect ~30 deleted dead-code test files);
  `tsc -b` clean; vitest 534/534; `vite build` clean; ESLint 0 errors / 105
  warnings (logged above); Playwright 375/375
- **Phase 2** (2026-09-25): `dotnet build SwebKit.slnx` clean; `dotnet test`
  1941/1941; `tsc -b` clean; vitest 534/534; `vite build` clean; ESLint 0
  errors / 102 warnings (three cleared by the dead-export cleanup); Playwright
  375/375. Knip dead-export sweep applied across `web/src` + `web/e2e` —
  2 dead store files, 13 dead functions/hooks, ~20 internal-only `export`s
  dropped. `labelSelector` is now `encodeURIComponent`'d in both pod-query
  call sites.
- **Phase 3** (2026-09-25): frontend-only change — `tsc` clean; vitest 534/534;
  `vite build` clean; ESLint 0 errors / 101 warnings; Playwright 375/375
  (dashboard + global-agent-panel + monitoring specs cover the reworked
  cockpit). No .NET or Rust changes.

## Findings Log

### Phase 0

- **Fixed inline**: 28 committed `artifacts/copilot-build/` binaries; ~560 legacy
  MAUI/WinUI/PocConsole files; `tests/SwebKit.E2E.Tests` (drove MAUI via WebView2 CDP);
  untracked `tests/SwebKit.WinUI.Tests` leftover; 11 unused NuGet `PackageVersion`s;
  2 unused npm deps (`@tauri-apps/plugin-dialog`, `plugin-clipboard-manager` —
  Rust-side plugin commands still work via `invoke()`); dead `.gitignore`/`.editorconfig`
  MAUI refs; dangling `blazor-maui.md` links in pitfalls.
- **CI gap fixed**: `src/SwebKit.Sql`/`tests/SwebKit.Sql.Tests` were in no paths-filter —
  Sql-only changes skipped all CI. Added to `shared_dotnet` + core job test step.
- **slnx was stale**: missing `src-sidecar`, `Sidecar.Tests`, `Agents.Tests` — added;
  solution-level build/test now covers the whole product.
- **Deferred to Phase 1**: `docs/architecture/` (architecture.md, codebase-guide.md,
  functionalities/*) still documents the MAUI app as live — needs rewrite against
  `web/` + `src-sidecar/`. `docs/environment-variables-redesign.md` references legacy
  paths — historical design doc, left as-is.
- Per-csproj unused `PackageReference` audit: shallow pass only (props-level cleanup
  done); a per-project audit is worth a Phase 1 look.

### Phase 1 (2026-09-25)

**Dead-code island — deleted after user approval (~7k LOC production + ~30 test files):**

Removed: `src/SwebKit.DevOps/` + `tests/SwebKit.DevOps.Tests/` entirely; incident-
timeline/deployment-assurance island (`IncidentTimelineService`,
`IncidentInvestigationSeedResolver`, `IncidentMappingProposalGenerator`,
`IncidentSnapshotExporter`, `PipelineFailureClassifier`, `RuntimeDriftService`,
`DeploymentValidationService`, `ApprovalAgingPolicy`, `ObservabilityExplainerService`,
plus per-lib adapters `AksTimelineSignalSource`, `AppInsightsTimelineSignalSource`,
`ServiceBusEvidenceSignalSource`, `DevOpsReleaseTimelineSignalSource`); dead plumbing
(`ConfigurationHealthService` 705 LOC, `TaskQueueService`, `ConnectionStateService`,
`PortForwardSessionService`, `TtlFormatter`, `RedisScanPageAccumulator`,
`RedisOpsInsightsAggregator`, `RequestBodyFormatter`, `VariablePreviewService`,
`WebSocketClientService`, `GraphQlSchemaService`, `GraphQlSubscriptionService`,
`BrunoSyncService`, `BrunoCollectionExporter`, `RedisImportParser`,
`RedisConnectionImportParser`, `ReleaseRepository`, `NotificationModels`,
`ToastNotificationResult`); dead abstractions (`IDevOpsClient*`, `IIncidentTimeline*`,
`IAksWarmupCache`, `IRedisWarmupCache`, `IServiceBusWarmupCache`,
`INotificationService`, `IWindowsNotificationService`, `IToastDiagnosticService`,
`IOAuth2TokenManager`, `ICollectionExporter`, `IGraphQl*`, `IWebSocketClientService`,
`IConfigurationHealthService`, `IConnectionStateService`, `IPortForwardSessionService`,
`IRequestBodyFormatter`, `IServiceBusNamespaceBootstrapper`, `ITaskQueue`,
`IVariablePreviewService`, `IIncident*`, `IObservabilityExplainerService`,
`IPodHealthMonitorService`); `DevOpsConfig`, `IncidentTimelineConfig`,
`AppConfig.DevOps`/`IncidentTimeline` fields, `devOpsConfig` in `web` types,
`AppDataPaths.ReleasesJson`, devops/incident buckets in `LogFeatureBucketResolver`,
release fields in the config-bundle model, DevOps refs in `build.yml` + `slnx`,
agent system-prompt copy + context-builder fields.

**Restored — looked dead but are live (kept):** `NoopKeyVaultSecretResolver`
(tests' null-object), the *importer* halves of `PostmanCollectionExportImport` /
`SwebKitCollectionExportImport` + `ICollectionImporter` (API-client import flow),
`PortForwardSession` model (live AKS type — distinct from the deleted service),
`DemoModeService` and all `Demo*Client`s.

Original scan list preserved below for the record:

**Dead-code island — ~7k LOC of unwired production code kept alive only by tests:**

- `src/SwebKit.DevOps/` entire project (DevOpsClient, DevOpsClientFactory,
  DevOpsReleaseTimelineSignalSource, AdoApiModels, DevOpsAuthHandler ~1.2k) +
  `tests/SwebKit.DevOps.Tests` — zero refs in sidecar DI/endpoints, zero in `web/`
  (`devOpsConfig` unused in UI). `IDevOpsClient`/`IDevOpsClientFactory`/`DemoDevOpsClient`
  in Core are only referenced by the dead island itself.
- Incident-timeline feature island in `SwebKit.Core/Services`:
  `IncidentTimelineService`, `IncidentInvestigationSeedResolver`,
  `IncidentMappingProposalGenerator`, `IncidentSnapshotExporter`,
  `PipelineFailureClassifier`, `RuntimeDriftService`, `DeploymentValidationService`,
  `ApprovalAgingPolicy`, `ObservabilityExplainerService` — zero refs outside tests.
  `docs/architecture/functionalities/{releases,incident-timeline}.md` document them.
- Dead plumbing/utilities in `SwebKit.Core/Services`: `ConfigurationHealthService`
  (705 lines — never DI-registered, nothing consumes `IConfigurationHealthService`),
  `TaskQueueService`, `ConnectionStateService`, `PortForwardSessionService`
  (port-forward lives in `src-tauri/src/native.rs` now), `TtlFormatter`,
  `RedisScanPageAccumulator`, `RedisOpsInsightsAggregator`, `RequestBodyFormatter`,
  `VariablePreviewService`, `WebSocketClientService` + `GraphQlSchemaService` +
  `GraphQlSubscriptionService` (API-client WS is frontend-native), `BrunoSyncService`,
  `BrunoCollectionExporter`, `PostmanCollectionExportImport`,
  `SwebKitCollectionExportImport` + `ICollectionExportImport` (export is done in web
  via `zip.ts`/download helpers), `RedisImportParser`, `RedisConnectionImportParser`,
  `NoopKeyVaultSecretResolver`.
- Dead abstractions: `IAksWarmupCache`, `IRedisWarmupCache`, `IServiceBusWarmupCache`
  (MAUI-era warm-up; now `useWorkspaceWarmup` + TanStack prefetch),
  `INotificationService`, `IWindowsNotificationService`, `IToastDiagnosticService`,
  `ToastNotificationResult` (Windows toast — superseded), `IOAuth2TokenManager`.
- Leftover on-disk `bin/obj` from Phase-0-deleted projects removed (4 dirs, ~3.8k files
  — gitignored so invisible to `git status`, pure disk waste).

**Healthy / no action needed:**

- `Program.cs` is well-organized: grouped DI, singleton connection pools per feature,
  thoughtful global exception handler with secret-safe messages, per-feature
  `Map*Endpoints`. Only stale `MauiProgram.cs` comments (now fixed).
- Routes are `React.lazy` + `Suspense`; `manualChunks` splits react/query/icons and
  mermaid/cytoscape/codemirror are dynamic-import chunks — bundle is in decent shape.
- `git.rs` unwrap()s are all `#[cfg(test)]`; `native.rs`/`pod_shell.rs` use
  `Mutex::lock().unwrap()` — idiomatic. Rust shell OK.
- `AppInsightsDiscoveryService` **is** wired (Program.cs line ~175 +
  `ObservabilityEndpoints`) — the prior review's "unwired" note is stale.

**ESLint debt (0 errors, 105 warnings — candidates for Phase 2 per-feature fixes):**

- `react-hooks/refs` ×23 — refs written/read during render (`useMonitoring`,
  `useContextualAgent`, `screen-state.ts`, …): real render-phase violations;
  `useEffectEvent`/effect pattern fixes belong to each feature's deep dive.
- `react-hooks/set-state-in-effect` ×31 — cascading-render pattern; most are
  intentional reset-on-key-change idioms, triage per feature.
- `react-refresh/only-export-components` ×31 — fast-refresh hygiene, cosmetic.
- `react-hooks/static-components` ×7, `incompatible-library` ×4, `immutability` ×4,
  `preserve-manual-memoization` ×3, `exhaustive-deps` ×3, `purity` ×2.

### Phase 2 — feature deep dives

#### AKS (8.8k web + ~6.5k `KubernetesAksClient` partials + 715-LOC endpoint file)

**Fixed inline:** deleted `GET /api/aks/{ns}/pods/{name}/logs` (non-stream) — zero
callers; every log view goes through `/logs/stream` SSE, and the endpoint still
carried the required-`int tail` binding trap the stream variant had fixed.

**Findings:**

- **`AksWorkspaceContext` god-context defeats memoization** — one context value
  carries ~70 fields including high-churn entries (`isAksFetching`,
  `lastRefreshedAt`, `contextMenu`, `pendingConfirm`). Every consumer re-renders
  on each 10s auto-refresh settle, and every `useMemo`/`useCallback` dep'd on
  `ws` (most of them, e.g. `AutoscalingTab`'s tables) recomputes — so the
  careful memoization + `ResourceTable`'s `memo()` are largely inert.
  **Proposal (flagged):** split into stable-action context, selection context,
  and churn context (`lastRefreshedAt`/`isAksFetching`/menus) so panels don't
  re-render per refresh tick.
- **Unused DI params** — `ProfileRepository`/`DemoModeService` are injected into
  ~30 AKS handlers that never use them (kept by force of habit); same pattern
  repeats across endpoint files. Mechanical cleanup, deferred — touches all
  endpoint tests' call sites for cosmetic gain.
- **Tab duplication** — `HpaTable`/`ScaledJobsTable` are near-identical
  (confirm-flow + menu-builder + columns boilerplate); the same shape repeats
  across ~15 tabs. A shared "resource actions table" abstraction is a Phase 2+
  candidate, not now.
- `DemoAksClient.cs` 2868 LOC — demo data, acceptable but the largest single
  file in Core; a per-domain split (`DemoAksClient.Pods.cs` etc.) would help.
- Positive: `useAks.ts` is exemplary — context-scoped query keys, mutation
  notifications via `useNotifyMutation`, staleTime on slow calls. Endpoint file
  documents prior bugs well. K8s client catches are all purposeful; signal
  sources + version caches are correctly instance-scoped.

#### API Client (8.6k web + 276-LOC endpoint file + ~841 LOC Core services)

**Findings — healthy, no action:**

- `ApiClientPageContext` (1134) is the largest god-context but is unusually
  well-structured: preview-tab semantics, serialized `useUpdateCollections`
  scope + `concurrencyToken` conflict handling, transient `credentialSecret`
  scrubbed before persistence. The split flag still applies (churn fields like
  `tabStates`/`confirmDialog`/`nameDialog` re-render everything) but this one
  earns its complexity more than the others.
- Endpoints are thin and honest (explicit "no catch here" note where the global
  handler does a better job); credential endpoints mask secrets and never echo
  raw values.
- ESLint: 23 warnings in feature scope — 21 `react-refresh/only-export-components`
  + 1 `react-hooks/refs` (`BodyCodeEditor`'s `onChangeRef.current = onChange` in
  render — the standard latest-callback pattern, worth an `useEffectEvent` swap
  when that file is next touched).
- Good coverage: `HttpRequestExecutor*`, `VariableService`, `PostRequestCapture`,
  collection repo/import/export all have dedicated test files.

#### Service Bus (6.3k web + 495-LOC endpoint file + 872-LOC `AzureServiceBusClient`)

**Findings — healthy, no action:**

- Post-overhaul the feature is in good shape: peek count is clamped server-side
  (`MaxPeekCount = 250`, the huge-queue 500 fix), extracted handlers are unit
  tested for exactly-once mutation, `entitySegment()` encoding is documented,
  `invalidateServiceBusQueries` centralizes key-prefix discipline.
- `useServiceBus.ts` is the best-documented hook file in the repo (per-query
  staleTime rationale, cache-read `placeholderData` for entity stats).
- Minor API asymmetry, cosmetic only: `/complete` + `/deadletter` take `long[]`
  bodies while `/dlq/complete`, `/resubmit`, `/resend` wrap `string[]` in a
  request record. Both sides already agree — harmonizing would be churn.
- `MessageList.tsx` (1566) is the feature's monolith — filter bar, saved
  filters, column toggle, chunked bulk ops and virtualized grid in one
  component — but internally factored and heavily commented. A
  `MessageListToolbar`/row-renderer split is a Phase 3 candidate, not a bug.
- ESLint in scope: 11 warnings (component-in-render warnings in `EntityTree`,
  1 ref-in-render, 1 setState-in-effect) — already counted in the repo total.

#### Settings / Storage / Redis / Monitoring / Agent / SQL / Layout

A knip dead-export sweep ran across `web/src` + `web/e2e`. Real removals:

- Deleted dead Zustand stores `stores/connection.ts` + `stores/selection.ts`
  (created, never imported — superseded by the per-feature page contexts).
- Deleted dead `api.ts` wrappers the hooks bypass: `setRedisHashField`,
  `deleteRedisHashField`, `updateRedisSortedSetScore`, `getAksResourceYaml`,
  `applyAksResourceYaml`, `validateAksResourceYaml` (hooks call the same
  endpoints via `apiSend`/`apiFetch` directly).
- Deleted dead hooks: `usePinnedResources`, `useRedisListItems`,
  `useRedisSetMembers` (non-paginated variants superseded by the paginated
  ones), `useSbBatchSend`, `useSbResendMessages`, `useSbDeadLetterMessages`
  (the chunked-bulk path calls `apiSend` directly so progress reporting stays
  per-chunk).
- Deleted dead `tauri-bridge` wrappers: `readClipboard`, `pickFile`,
  `confirmDialog`, `alertDialog`, `writeFile`, `listDir`, `listSecrets` (the
  Rust commands stay registered — harmless, and web fallbacks aren't needed).
- Deleted dead e2e helper `selectAksDefaultNamespace`.
- Dropped `export` from ~20 internal-only symbols (operator tables, filter
  helpers, tree utils, scenario builders, `AKS_KEY_PREFIX`, `KEYBOARD_SHORTCUTS`,
  `allTabs`, `Skeleton`, etc.) so knip's next pass reports real orphans only.
- `filterLogic.ts` re-exported `requiresPropertyName` while `AdvancedFilterPanel`
  imported it from `filterTypes` directly — dead re-export removed.
- Knip false positive kept: `cross-env` is used by `playwright.config.ts`'s
  webServer command.

**Findings:**

- **The god-context pattern is systemic, not AKS-only** — `StoragePageContext`
  (~100 fields, memoized), `ApiClientPageContext` (~75 fields),
  `AksWorkspaceContext` (~70 fields), and **`RedisPageContext` (~85 fields) —
  split done**: six churn-separated contexts (Connection/Nav/Queries/Browser/
  Editor/Ops), every handler `useCallback`'d, and query/mutation objects travel
  through stable facades (`web/src/lib/queryFacade.ts`) so unrelated renders
  don't invalidate consumers. This is now the proven pattern to roll out to the
  remaining three contexts.
- No TODO/FIXME/HACK anywhere in `src-sidecar/`, `web/src/`, or `src/` —
  hygiene is enforced.
- Empty catches found are all process/file cleanup (`AcpJsonRpcPeer`,
  `AppDataFileStore`) — intentional.
- Endpoint files stay thin and well-organized; SQL endpoints cap row counts
  (`HardMaxRows=5000`), JSONPath bodies are bounded (8 MB), secrets are masked
  before returning. Service Bus count clamp, connection-test sanitizing, and
  demo-id stripping in `ConfigEndpoints.SaveProfileAsync` are all already
  correct.

**Remaining flagged items (deferred to Phase 2 / later):**

- `*PageContext.tsx` god-contexts: Storage 1202, ApiClient 1134, Aks 1049 remain —
  apply the Redis split pattern (per-churn contexts + `lib/queryFacade` facades +
  `useCallback` handlers).
- `web/src/lib/types.ts` (1516) — flat bag of ~159 types mirroring sidecar contracts;
  per-domain split is cosmetic, low priority.
- `web/src/lib/api.ts` (796) — transport (`apiFetch`/`apiSend`/`apiUpload`/
  `streamAgentChat`) mixed with ~60 domain endpoint functions; worth splitting into
  `lib/api/<domain>.ts` when touched — not urgent.
- `docs/architecture/functionalities/*.md` — several remain MAUI-era in detail
  (`SwebKit.App` razor paths, Blazor flows); dead-feature references stripped this
  phase, full rewrites land with each Phase 2 deep dive.
- `git.rs` (1.3k) — command table + parsing in one file; split candidate if the
  git surface grows.
- Config-readiness/probe feature (`ConfigurationHealthService`/`ConfigurationProbeService`)
  existed only in the deleted MAUI app — noted as a parity gap to consider when the
  Settings deep dive lands in Phase 2, not a bug.
