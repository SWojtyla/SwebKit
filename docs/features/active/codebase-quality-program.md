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

- [ ] Design proposal presented for approval before implementation — cockpit
  ingredients from the prior review: proactive-insights feed with Investigate,
  workspace topology graph (cytoscape), pinned agent conversation, NL command bar;
  keep health tiles/pins

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

**Remaining flagged items (deferred to Phase 2 / later):**

- `*PageContext.tsx` god-contexts: Storage 1202, ApiClient 1134, Aks 1049, Redis 881 —
  split proposal lands with each feature's Phase 2 deep dive.
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
