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

- [ ] **`docs/architecture/` rewrite** — `architecture.md`, `codebase-guide.md`, and
  most `functionalities/*.md` still describe the MAUI app's file paths as the live
  implementation. Rewrite against `web/` + `src-sidecar/` (found in Phase 0 sweep).

- [ ] Sidecar: `Program.cs` DI wiring audit (lifetime/pooling per
  `docs/pitfalls/azure-sdk.md`), endpoint organization, error-handling consistency,
  config/profile store boundaries
- [ ] Shared libs: `SwebKit.Core` 28.6k — demo clients may deserve their own
  folder/assembly; verify `SwebKit.DevOps` residual usage; `LinkedCollectionFileService.cs` 1.4k
- [ ] Web: `*PageContext.tsx` god-context pattern (evaluate split — flag proposal);
  `lib/types.ts` monolith (per-domain files?); `api.ts` layering; zustand vs React
  Query vs context consistency
- [ ] Bundle/perf: `manualChunks` for mermaid/cytoscape/codemirror; route-level lazy
  loading check; re-render hotspots; query `staleTime`/`gcTime` sanity
- [ ] Rust shell: `unwrap()` audit, `git.rs` 1.3k structure

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
  Sql 46, DevOps 29); vitest 534/534; `vite build` clean; Playwright: _running_

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
