# Test Plan — Coverage Expansion & CI

Companion to [production-readiness-review.md](production-readiness-review.md) and
[technical-plan.md](technical-plan.md). This supersedes `docs/plans/test-coverage-expansion.md` as
the active source of truth — that doc's baseline numbers are stale (see
[technical-plan.md](technical-plan.md) §1.4); archive it once this plan is in place.

## Re-baselined current state (2026-08-01)

| Layer | Current count | Notes |
|---|---|---|
| Playwright e2e | 22 spec files, ~178 tests | Deep on api-client/service-bus/redis/storage; thin on settings (6), agent (6), monitoring (8), navigation (1) |
| Vitest unit | 9 files | Deliberately pure-logic only (`environment: "node"`, no DOM) — a repo-level design decision, not an oversight; see `web/vitest.config.ts`'s own comment |
| Sidecar (xUnit) | 3 files | Covers only API-client preview handler, monitoring eval engine, KeyVault resolver — 8 of 9 endpoint files and 5 of 6 services have zero coverage |
| `SwebKit.Core.Tests` | ~75 files vs. 181 src files | Broad, mature |
| `SwebKit.Azure.Tests` | 8 files vs. 26 src files | Thinner |
| `SwebKit.Kubernetes.Tests` | 6 files vs. 27 src files | Thinner |
| `SwebKit.Agents.Tests` | 12 files vs. 38 src files | Reasonable |
| Rust (`src-tauri`) | 0 tests | `git.rs` alone is 1291 lines / 15 commands with no dedicated tests |
| Accessibility | 0 tests | No `axe-core` or equivalent anywhere |
| CI | Runs .NET tests (Core/Azure/Kubernetes/DevOps/Agents/MAUI/sidecar) + Playwright e2e | Does **not** run Vitest or any Rust check |

---

## 1. Sidecar endpoint/service test coverage (highest priority — see review §1.6)

The test harness for this already exists and is proven: `tests/SwebKit.Sidecar.Tests.csproj` carries
a `FrameworkReference` to `Microsoft.AspNetCore.App` specifically so minimal-API handlers can be unit
tested directly (no `WebApplicationFactory` needed), demonstrated end-to-end in
`ApiClientEndpointsPreviewTests.cs` (extract the handler body to an `internal static` method, test it
directly with a fake dependency). Apply the same pattern to the remaining files, roughly in order of
risk (mutating-state endpoints first):

1. **`AksEndpoints.cs`** (509 lines) — blocked on [technical-plan.md](technical-plan.md) §3.1 (replacing
   the static mutable `IAksClient` singleton) landing first, since a static field can't be substituted
   with a fake `IAksClient` in a unit test. Once unblocked: test the request/response mapping and
   demo-mode branching for at least Deployments, Pods, HPA, and the `/httproutes` exception-handling
   fix from §3.2 (assert a client-throw surfaces as an error response, not a silent empty array).
2. **`RedisEndpoints.cs`** (505 lines) — the other endpoint file mutating state (key writes, TTL,
   rename, delete). Extract handler bodies per the established pattern; test success + not-found +
   client-error paths for at least the mutation endpoints (hash/list/set/zset writes, rename, TTL).
3. **`ServiceBusEndpoints.cs`** (383 lines) — peek/send/complete/purge/DLQ-resubmit paths, especially
   given the review's finding that a notification-duplication bug might indicate a mutation firing
   twice ([ux-plan.md](ux-plan.md) Phase 0.1) — a handler-level test asserting exactly one send/resubmit
   call reaches the underlying client per invocation is valuable regression coverage here specifically.
4. **`StorageEndpoints.cs`** (344 lines) — upload/copy/metadata/versions/SAS/undelete, all confirmed
   wired but untested.
5. **`MonitoringEndpoints.cs`** (114 lines) — CRUD + SSE stream; the evaluation engine itself already
   has tests (`MonitoringAlertEvaluationServiceTests.cs`), this closes the endpoint-layer gap.
6. **`AgentEndpoints.cs`** (46 lines) — smaller, but will grow once tool-calling lands
   ([ux-plan.md](ux-plan.md) §2.1) — write tests alongside that feature, not before, so they cover the
   real shape rather than the current tool-less stub.
7. **`ConfigEndpoints.cs`** — test the export/import round-trip, and specifically add a regression test
   for [technical-plan.md](technical-plan.md) §3.6 (the `CredentialSecret` structural guard) once that
   lands: assert a `PUT /api/config/collections` body carrying a populated `CredentialSecret` field
   never reaches disk with the secret intact.
8. **`DemoModeService.cs`** — test alongside §3.3's centralized demo-mode resolver refactor.

For services: `SidecarAgentChatService.cs`, `SidecarAuthHeaderBuilder.cs`,
`SidecarCredentialStore.cs`, `SidecarMonitoringConnectionPool.cs` currently have zero direct tests
(only indirectly exercised via endpoint/e2e tests). Prioritize `SidecarAuthHeaderBuilder.cs` (security-
sensitive secret-resolution precedence logic deserves direct unit tests, not just indirect coverage)
and `SidecarMonitoringConnectionPool.cs` (the pattern §3.1 wants `AksEndpoints.cs` to adopt — lock in
its current correct behavior with tests before other code starts depending on it more).

Add a **`Program.cs` integration smoke test** (a real `WebApplicationFactory<Program>`-based test, the
one place a full host is actually warranted) asserting: the global exception handler maps a thrown
exception to the expected status code and a *sanitized* body (regression test for §3.5), and CORS
rejects a non-allowed origin while accepting `tauri://localhost`.

## 2. Web unit test gaps

Vitest's pure-logic-only scope is a deliberate, defensible design (see `vitest.config.ts`'s comment) —
do not add a DOM/jsdom environment or component-render tests without a separate decision (§4 below).
Within the existing scope, these `lib/*.ts` files have no coverage and are large/central enough to
warrant it:

- **`lib/hooks.ts`** (1540 lines) — not directly unit-testable as React Query hooks without a
  component-test tier, but do this split alongside [technical-plan.md](technical-plan.md) §2.4's
  per-domain split: as each domain module is extracted, pull out any pure helper functions (query-key
  builders, response-shape mappers) into their own testable functions rather than leaving them as
  closures inside the hook.
- **`lib/api.ts`** (347 lines) — the `extractErrorMessage`-style parsing logic (already added once for
  the Key Vault preview flow) and the `apiFetch`/`apiSend` error-path behavior are pure enough to unit
  test with a mocked `fetch` — add tests covering: non-JSON error bodies, ProblemDetails-shaped bodies,
  plain-`{error}`-shaped bodies, and the happy path.
- **`lib/tauri-bridge.ts`** (349 lines) — the parts that aren't direct Tauri IPC calls (any
  request/response shaping, retry/fallback logic) should get unit tests; the IPC calls themselves are
  better covered by e2e (they need a running Tauri context, which the current e2e setup doesn't
  actually provide — see the `restart_sidecar`-is-never-called finding, which a test here would have
  caught structurally by asserting *something* calls it).

## 3. E2E coverage gaps by feature area

| Area | Current tests | Gap |
|---|---|---|
| Settings | 6 | Only covers tab-switching and a couple of fields; no coverage of the AKS "demo mode inert" banner, the Key Vault section (added this session, has coverage — verify it's counted here), or the dead DevOps tab (which Phase 0.8 of [ux-plan.md](ux-plan.md) removes — don't add tests for it, remove the removal's test debt instead) |
| Agent | 6 | No coverage at all for tool-calling once [ux-plan.md](ux-plan.md) §2.1 lands — add tests alongside that feature, not retroactively |
| Monitoring | 8 | No coverage of the raw-severity-`0` display bug (review §3 finding #15) — add a regression test once fixed |
| Navigation | 1 | Thinnest file in the suite for what should be a simple, stable check — expand to cover every nav item + browser back/forward, matching the depth already present in `service-bus-url-state.spec.ts`/`aks-url-state.spec.ts` for other areas |

Also add **regression tests for every Phase 0 bug fix in [ux-plan.md](ux-plan.md)**, not as an
afterthought but as part of each fix's own PR:
- 0.1 (key-collision duplicate render) — assert exactly one notification/row renders per action.
- 0.2 (nested button) — assert no React console error/warning during a Service Bus entity-tree
  interaction (Playwright can assert on `page.on("console", ...)` for error-level messages, matching
  the pattern the UX-review agent used manually here — worth adding as a standing assertion, not just
  spot-checked by hand).
- 0.3 (wrong demo port) — assert the bundled demo request's URL matches the resolved sidecar port.
- 0.5 (AKS demo-mode "not configured" inconsistency) — assert Dashboard/Settings/footer all agree AKS
  is "ready" when demo mode is on.
- 0.6 (narrow-viewport clipping) — extend `api-client-layout.spec.ts`'s existing narrow-width test to
  also assert the response pane content is reachable.
- 0.7 (Pod YAML shows Deployment fields) — assert a Pod's YAML view contains `kind: Pod` and pod-shaped
  fields (`status.phase`), not `replicas`/`selector`.

## 4. Component-test-tier decision

Currently, all component behavior verification lives in Playwright e2e (serial, `workers: 1`, 60s
timeout for cold Vite compiles). This is a defensible tradeoff today at 22 spec files but is a
scaling risk as coverage grows alongside the app becoming the primary daily-driver tool (slower
feedback loop, harder to pinpoint exactly which component regressed vs. which page-level flow broke).

**Decision needed, not pre-decided by this plan**: evaluate adding a fast component-test tier (React
Testing Library + jsdom, as a second Vitest config or project) for the highest-value, highest-risk
components identified in this review — the newly-decomposed `RedisPage`/`StoragePage` tab components
(Module 2 of [technical-plan.md](technical-plan.md)) would be natural first candidates, since
decomposition is happening anyway and testing the extracted pieces in isolation is cheaper than
re-verifying them through a full page load each time. If the decision is "no, e2e is enough" — that's
a legitimate outcome too, but make it a deliberate decision recorded here (or in an updated
`vitest.config.ts` comment) rather than defaulting by inertia.

## 5. Accessibility testing

Zero accessibility tests exist anywhere. Add `@axe-core/playwright` (or equivalent) to `web/package.json`
and:
- Add an axe scan to at least one e2e test per major feature page (a smoke-level "no serious/critical
  violations" assertion is enough to start — don't attempt zero-violations everywhere immediately,
  especially given [ux-plan.md](ux-plan.md) Phase 4's manual a11y fixes are still in progress in
  parallel).
- Once [ux-plan.md](ux-plan.md) Phase 4 lands the `role="dialog"`/`aria-modal` fixes and tree/list ARIA
  roles, tighten the axe assertions for those specific components to genuinely zero violations, so the
  fix has a regression guard.
- Add a contrast-check assertion (axe covers this) specifically for the AKS pod-status light-theme fix
  from [ux-plan.md](ux-plan.md) Phase 4.3.

## 6. Rust testing

`src-tauri/src/git.rs` (1291 lines, 15 commands) has zero dedicated tests. Add `cargo test` coverage
for its pure logic first (diff/status output parsing, path-validation helpers in `native.rs`'s
`validate_within_roots`/`validate_dir_within_roots`) before attempting to test the subprocess-spawning
commands themselves (which need either a real git repo fixture or a mock process runner — heavier
investment, schedule after the pure-logic tests prove the harness works).

## 7. CI wiring

`.github/workflows/build.yml`'s `frontend` job currently only runs `npm run build` (typecheck +
bundle). Add:
- `npm run test:unit` (Vitest) as a required step in the `frontend` job.
- A new `rust` job (or extend an existing one): `cargo test` + `cargo clippy -- -D warnings` for
  `src-tauri`, gated by `dorny/paths-filter` the same way the other jobs are, so it only runs when
  `src-tauri/**` changes.
- Once §5's accessibility tests exist, ensure they run as part of the existing `e2e` job (they're
  Playwright-based, no new job needed).
- Consider whether the existing non-blocking (`continue-on-error`) vulnerability scan step should be
  made blocking once this plan's security items (Module 3 of [technical-plan.md](technical-plan.md))
  land and a clean baseline is established.

## 8. Security scan process

Per `docs/security/aikido-mcp-scan.md`, run the `aikido_full_scan` process on all new/modified
first-party code touched by this plan, fix-and-rescan until zero findings — apply this per-module as
work lands, not as one giant scan at the end.
