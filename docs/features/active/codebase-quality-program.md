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

- [ ] Delete `src/*/artifacts/copilot-build/**` (28 committed binaries); add
  `artifacts/` to `.gitignore`
- [ ] Delete `src/SwebKit.App/`, `src/SwebKit.WinUI/`, `src/SwebKit.Agent.PocConsole/`,
  `tests/SwebKit.App.Tests/`, `scripts/maui/`
- [ ] Remove MAUI job + `maui` filter from `.github/workflows/build.yml` paths-filter
  (keep `shared_dotnet` — core/sidecar jobs still need it)
- [ ] Remove deleted csprojs from the solution file(s) and MAUI-only packages from
  `Directory.Packages.props` (FluentUI, MAUI packages — only if exclusively used by
  deleted projects)
- [ ] Delete or rewrite `docs/packaging-and-install.md` (MAUI-only doc); update
  `scripts/README.md`, `README.md` legacy section, `CLAUDE.md` legacy list,
  `docs/context.md` dir map, `docs/pitfalls/blazor-maui.md` (delete or mark historical)
- [ ] Dependency audit: unused npm deps in `web/package.json`, unused NuGet refs
- [ ] Close out `docs/features/active/aks-storage-ux-improvements.md` and the stale
  folder-style plans (`ai-insight-reports/`, `aks-multi-context-alerting/`) — shipped;
  fold durable learnings into `docs/pitfalls/`, delete files, drop catalog lines

### Phase 1 — Global architecture

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

_Appended per phase._

## Findings Log

_Appended per phase._
