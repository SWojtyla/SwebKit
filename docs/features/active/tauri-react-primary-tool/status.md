# Status — Tauri + React as the Primary Tool

## Current State

`In Progress`

**Jira:** not linked

## Progress

- [x] Research complete: 6 parallel passes (existing-docs synthesis, web app inventory, MAUI
      parity gap analysis, sidecar code-quality audit, test-coverage/packaging audit, hands-on UX
      review of the running app)
- [x] `production-readiness-review.md` written — consolidated findings, ranked
- [x] `ux-plan.md` written — phased UX/UI/functionality plan
- [x] `technical-plan.md` written — phased clean-code/architecture/performance/security plan
- [x] `test-plan.md` written — phased test-coverage and CI plan
- [x] Docs-hygiene fixes (technical-plan.md Module 1) — done: 6 dangling references resolved
      (recreated as `docs/features/archive/*` stubs), `docs/features/README.md`'s stale canonical
      order fixed, `architecture.md`/`design.md` given primary-stack notices, MAUI-only review docs
      and `environment-variables-redesign.md` scope-noted, `test-coverage-expansion.md` re-baselined
- [x] Phase 0 critical bug fixes (ux-plan.md) — done, except 0.3/0.4 which turned out to not be
      code bugs (see note below): notification key-collision (0.1), invalid nested `<button>` in
      Service Bus entity tree (0.2), AKS "not configured" inconsistency on Dashboard/Settings (0.5),
      response-panel clipping at narrow widths (0.6), Pod YAML showing Deployment-shaped content
      (0.7), dead DevOps settings tab removed (0.8)
- [ ] Phase 1: land uncommitted work (`monitoring-rebuild`, `api-client-git-completion`,
      `api-client-ux-overhaul`) — **could not do**: the branch these were implemented on
      (`feat/api-client-ux-and-git`) does not exist in this repository, locally or on origin, as of
      this pass. Either it was never pushed from wherever that work happened, or those status docs
      describe work that was never actually committed anywhere reachable here. Re-verify with
      whoever ran those sessions before assuming this work still exists to "land."
- [x] Sidecar architecture & security fixes (technical-plan.md Module 3) — **partial**: done —
      static mutable `IAksClient` singleton replaced with the DI-registered
      `IMonitoringConnectionPool` pattern (§3.1), `/httproutes` no longer swallows all exceptions
      (§3.2), `CredentialSecret` now has a structural strip-before-save guard, not just a
      conditional `JsonIgnore` (§3.6). Not done: §3.3–3.5, 3.7–3.11 (demo-mode centralization,
      config-endpoint extraction, error sanitization, cancellation tokens, request validation,
      auth-builder consolidation, OpenAPI surface, shared-library re-verification).
- [x] Sidecar test coverage (test-plan.md Module 1) — **partial**: added
      `SidecarMonitoringConnectionPoolAksTests.cs` (6 tests covering the AKS resolution/caching path
      `AksEndpoints` now depends on). The 8 endpoint files and 4 remaining services with zero
      coverage are still zero — this needs either extracting handler bodies (the proven pattern) or
      a `WebApplicationFactory` integration harness, neither of which fit in the remaining time.
- [x] CI wiring (test-plan.md Module 7) — done: Vitest now runs in the `frontend` job, a new `rust`
      job runs `cargo clippy`/`cargo test` gated on `src-tauri/**` changes.
- [ ] Frontend architecture decomposition (technical-plan.md Module 2) — not started
      (`RedisPage.tsx`/`StoragePage.tsx` decomposition, `lib/hooks.ts` split,
      `ApiClientPage.tsx`/`RequestEditor.tsx` — the last two are also blocked on the same missing
      branch as Phase 1 above)
- [x] Shared UI primitives (technical-plan.md Module 4) — done: `Dialog`, `EmptyState`, `Skeleton`,
      `QueryState` added under `web/src/components/shared/`; migrated onto in API Client dialogs,
      collection export dialog, AKS alert-rule dialog, keyboard-shortcuts panel; command palette and
      entity command palette given `role="dialog"`/`aria-modal` directly (kept their existing
      custom focus/Escape logic instead of a full migration).
- [x] Performance pass (technical-plan.md Module 5) — **partial**: `RedisPage.tsx`'s key tree and
      `StoragePage.tsx`'s blob list now render through `@tanstack/react-virtual`, matching the
      pattern already proven in `CollectionTree.tsx`. Not done: the systematic `React.memo` pass
      once list rows are extracted, and `lib/hooks.ts`/large-component decomposition (Module 2,
      below) that the technical plan treats as a performance-adjacent prerequisite.
- [x] Production/ops readiness (technical-plan.md Module 6) — **partial**: sidecar file logging via
      `FileLoggerProvider`/`AppBootstrap` (moved to `SwebKit.Core.Diagnostics` so both MAUI and the
      sidecar share it), sidecar 500s now log the real exception server-side while still returning a
      generic message to the client, and a footer "Reconnect" button now calls the pre-existing
      `restart_sidecar` Tauri command when `useHealth()` detects an outage. The Tauri capabilities
      file (§6.6) remains deliberately **skipped**: adding an explicit capabilities file risks
      silently breaking the IPC bridge (wrong permission scoping) and this pass had no way to verify
      it against the real packaged desktop app — do this with a live Tauri build to test against,
      not blind.
- [x] UX Phase 2 feature parity (ux-plan.md) — **partial**: Storage batch download now bundles
      selected blobs into a single ZIP (`buildZip`, reusing the pattern already proven in Service
      Bus's `MessageList.tsx`) instead of firing one sequential download per file. Agent tool-calling
      is now wired for Kubernetes and Service Bus diagnostics: `Program.cs` registers the 6
      Kubernetes + 3 Service Bus read-only `IAgentTool` implementations (plus `DemoAksClient`,
      `IAgentToolRegistry`) and `SidecarAgentChatService` passes them to `IAgentModelClient.ChatAsync`
      when the active profile's capability is `ToolCalling`, gated exactly like MAUI's
      `AgentChatService.FilterToolsByCapability`. Deliberately **not** wired: the 2 Observability
      tools (`QueryLogsTool`/`GetMetricsTool`) — the sidecar has no `IObservabilityProviderFactory`
      at all yet, that implementation lives only in `SwebKit.App` (a separate, larger feature gap);
      and the 3 API Client mutation/proposal tools in `ApiClientTools.cs` — those need the
      confirmation-card flow (`IAgentActionCoordinator`/`AgentActionApplier`) that the sidecar's chat
      UI doesn't implement, so wiring them without it would let the model mutate collections with no
      user confirmation step. Real port-forward is done: `start_port_forward` now spawns an actual
      `kubectl port-forward -n <ns> pod/<pod> <local>:<remote>` subprocess (mirroring the
      stdout-parsing pattern `sidecar.rs` already used to recover an OS-assigned port), keeps the
      child in the session so `stop_port_forward` can kill it, and `RunEvent::Exit` now kills every
      live forward so none survive as orphans after the app closes. Still open: pod shell exec
      (`PodsTab.tsx`'s "Open shell in pod" is `disabled: true`) — deliberately **not** attempted this
      pass: it needs a pty crate (`portable-pty`), a bidirectional Tauri IPC streaming channel, and a
      brand-new xterm.js terminal component in the frontend (there is currently zero terminal/pty
      infrastructure anywhere in `web/` or `src-tauri/`), which is a multi-day feature in its own
      right, not a gap-closing fix — it needs its own design pass before implementation.
- [x] UX Phase 4-5 accessibility + visual polish — **done**: notification key collisions, nested
      `<button>`, and Redis key-tree keyboard reachability were fixed earlier. This pass added real
      `role="tree"`/`role="treeitem"`/`aria-expanded`/`aria-level`/`aria-selected` plus roving
      Arrow-key/Enter/Space navigation to `CollectionTree` (API Client) and `EntityTree` (Service
      Bus); `ResourceTable` (AKS) stayed a plain `<table>` (it has no sortable headers to give
      `aria-sort`) but its rows are now keyboard-reachable/selectable. It also migrated every
      remaining raw-color status indicator (`text-green/yellow/amber/red-*`,
      `bg-green/yellow/red-*`, including opacity variants) to the semantic
      `text-success/warning/destructive`/`bg-success/warning/destructive` tokens across 27 files —
      deliberately leaving alone the handful of genuinely decorative/categorical colors (Redis's
      per-datatype `typeColors` map, the TTL progress bar, two terminal-style diff/content viewers,
      the JSON syntax highlighter) that aren't status indicators.

## Verification

| Check | Result |
| --- | --- |
| `npx tsc -b` (web) | Pass |
| `npm run test:unit` (Vitest) | 116 passed |
| `npx playwright test` (full e2e suite) | 191 passed, 0 failed |
| `dotnet build` — `src-sidecar`, `SwebKit.Core`, `SwebKit.Azure` | Pass, 0 warnings |
| `dotnet test tests/SwebKit.Sidecar.Tests` | 32 passed (22 existing + 6 connection-pool + 4 agent tool-calling) |
| `dotnet test tests/SwebKit.Core.Tests` | 798 passed |
| `cargo clippy --all-targets -- -D warnings` (`src-tauri`) | Pass, 0 warnings |
| `cargo test --lib` (`src-tauri`) | 53 passed (50 existing + 3 port-forward parsing) |

## Definition of Done

1. Every critical/high-severity bug found in `production-readiness-review.md` §3 is fixed and has a
   regression test.
2. Every feature-parity gap rated "Important" in §4 is closed or explicitly re-classified as an
   intentional substitution with a documented rationale.
3. The three uncommitted-but-complete features (`monitoring-rebuild`, `api-client-git-completion`,
   `api-client-ux-overhaul`) are committed, manually verified in the real Tauri shell, and merged.
4. Crash telemetry, sidecar file logging, and sidecar crash recovery are all in place
   (`technical-plan.md` Module 6).
5. CI runs Vitest and Rust checks in addition to its current .NET/Playwright coverage.
6. The sidecar's endpoint/service test coverage matches the standard already proven on
   `ApiClientEndpointsPreviewTests.cs` for all 9 endpoint files.
7. `docs/architecture/architecture.md`/`design.md` describe the Tauri+React+sidecar system as
   primary, and all dangling doc references identified in this review are resolved.
8. A packaging decision (code signing, auto-update, app identifier) is made and implemented or
   explicitly deferred with a reason, not left as an unaddressed placeholder.
