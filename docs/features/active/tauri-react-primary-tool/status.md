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
- [x] Phase 1: land uncommitted work (`monitoring-rebuild`, `api-client-git-completion`,
      `api-client-ux-overhaul`) — **abandoned, confirmed by the user (2026-08-01)**: the branch these
      were implemented on (`feat/api-client-ux-and-git`) does not exist in this repository, locally or
      on origin, across multiple checks. The user has confirmed it no longer exists — this was
      genuinely lost work, not merely unlanded — so this phase is dropped, not just blocked. See the
      "ABANDONED" note in `ux-plan.md`'s Phase 1 section. Anything that was gated on this phase
      landing (`ApiClientPage.tsx`/`RequestEditor.tsx` decomposition, `technical-plan.md` §2.3) is now
      unblocked and can be scoped fresh whenever it's picked up.
- [x] Sidecar architecture & security fixes (technical-plan.md Module 3) — **done, with 2 explicitly
      non-actionable exceptions (§3.9/§3.10, see below)** —
      static mutable `IAksClient` singleton replaced with the DI-registered
      `IMonitoringConnectionPool` pattern (§3.1), `/httproutes` no longer swallows all exceptions
      (§3.2), `CredentialSecret` now has a structural strip-before-save guard, not just a
      conditional `JsonIgnore` (§3.6). §3.5 (error sanitization) is **partial by deliberate choice**:
      the AKS/Redis/Service Bus/Storage "test connection" + AKS context-switch endpoints no longer
      return `ex.Message` (see the sidecar-test-coverage entry above — this is where that gap was
      found and fixed). `ApiClientEndpoints.execute`'s `Results.Problem($"Request failed:
      {ex.Message}")` was deliberately left alone: unlike a connection-test probe, this endpoint's
      whole purpose is executing a user-authored HTTP request and showing why it failed (connection
      refused, DNS failure, timeout) — that's the same debugging signal Postman/Bruno/curl all
      surface, and it's about connectivity to a user-chosen target URL, not a credential-bearing SDK
      call, so the leak risk profile is different from the connection-test endpoints. §3.4 (move
      config endpoints out of `Program.cs`) is **done**: `/health`/`/api/demo-mode` moved to a new
      `SystemEndpoints.cs`; `/api/config/profiles`, `/api/config/environments`,
      `/api/config/collections`(+`/store`), `/api/config/user-settings` moved into the existing
      `ConfigEndpoints.cs` alongside export/import and the already-extracted `SaveCollectionsAsync`.
      `Program.cs` now just calls `MapSystemEndpoints()`/`MapConfigEndpoints()` — every domain now
      follows the same `Endpoints/*.cs` extension-method pattern, no exceptions left. §3.7
      (propagate `CancellationToken` consistently) is **done**:
      `RedisEndpoints.cs`/`StorageEndpoints.cs`/`ServiceBusEndpoints.cs` (0 → 24/14/16 occurrences)
      and the `ApiClientEndpoints.execute` handler now thread a real token from the request down to
      every `IRedisClient`/`IStorageClient`/`IServiceBusClient`/`IHttpRequestExecutor` call, which
      already declared and documented `CancellationToken` support on every method — this was pure
      plumbing, no logic change, matching `AksEndpoints.cs`'s pre-existing pattern. §3.3 (demo-mode
      centralization) is **explicitly declined, not just unstarted**: read the actual duplication
      across `RedisEndpoints.cs`/`StorageEndpoints.cs`/`ServiceBusEndpoints.cs` and it's a 2-line
      `if (demo.IsDemoMode) return demoThing; else return realThing;` branch that already differs in
      shape per domain (Redis's client factory is async, Storage/Service Bus's are sync; each
      "resolve the config/entity" step has a different signature) — and AKS uses a completely
      different pattern already (`IMonitoringConnectionPool`), so it wouldn't even participate in a
      shared abstraction. A generic `IDemoAwareClientResolver<TClient>` would need type parameters or
      delegates per domain either way, adding a layer of indirection without actually shrinking the
      real code. Not worth building — three similar 2-line branches are clearer than the abstraction.
      §3.11 (re-verify MAUI-era shared-library issues from `codebase-review-2026-07-18`) is **done —
      all three already fixed, striking them from this plan per the plan's own instruction**: (1)
      kubectl argument hardening — `KubernetesAksClient.cs` and `KubectlShellLauncher.cs` already
      build args via `ProcessStartInfo.ArgumentList` throughout (a dedicated
      `KubectlArgumentBuilder.cs` exists for this), not the raw single-string API the review flagged;
      (2) the two direct `DefaultAzureCredential` call sites in `KubernetesAksClient.cs` (lines 272,
      1504 as of this check) already go through `AzureCredentialFactory.CreateDefault(...)`, not a
      bypass; (3) `WindowsCredentialStore.cs`'s catch blocks already log via
      `logger?.LogDebug`/`LogWarning` on every path (Get/Delete/ListKeys) rather than silently
      swallowing — this was either already fixed before this initiative started or was a stale
      finding in the original review. §3.8 (request validation) is **done, scoped to the concrete
      gap that actually exists**: checked the plan's own examples (malformed GUIDs/enums causing an
      unhandled 500) and found the codebase already guards those — `Guid.TryParse` everywhere it's
      used, no unguarded `Enum.Parse`/`Guid.Parse`/`int.Parse` in any endpoint file. The real gap:
      `RenameKeyAsync`/`SetHashFieldAsync`/`DeleteHashFieldAsync`/`UpdateSortedSetScoreAsync` passed
      their request's required string field straight to the Redis client with no
      `IsNullOrWhiteSpace` guard (the DTO's `= string.Empty` default doesn't protect against a client
      explicitly sending `null` for that field — `System.Text.Json` overwrites it) — fixed using the
      same guard pattern already established in `StorageEndpoints.cs`. Module 3 is now closed out:
      **not done, and not expected to be soon**: §3.9 (auth-builder consolidation) explicitly says to
      wait until MAUI is far enough into deprecation that its `AuthHeaderBuilder` isn't being
      actively modified — not yet true; §3.10 (OpenAPI surface) is blocked on a vulnerable transitive
      dependency (`Microsoft.OpenApi` 2.0.0 via `Microsoft.AspNetCore.OpenApi`, GHSA-v5pm-xwqc-g5wc) —
      already attempted and reverted earlier in this initiative, revisit once a patched version
      ships.
- [x] Sidecar test coverage (test-plan.md Module 1) — **done, with 2 deliberate exceptions**: added
      `SidecarMonitoringConnectionPoolAksTests.cs` (6 tests), `SidecarAgentChatServiceToolsTests.cs`
      (4 tests), `AksEndpointsTests.cs` (11 tests: Deployments, Pods, HPAs, HTTPRoutes
      exception-propagation), `RedisEndpointsMutationTests.cs` (18 tests: hash field set/delete,
      sorted-set score update, rename, TTL), `ServiceBusEndpointsMutationTests.cs` (14 tests: peek
      active/DLQ, complete, purge, DLQ resubmit — complete/resubmit specifically assert the
      underlying client is invoked *exactly once*, regression coverage for the
      notification-duplication concern in ux-plan.md Phase 0.1; no double-invocation bug found at
      this layer, narrowing that concern to the frontend rendering layer), and
      `StorageEndpointsMutationTests.cs` (19 tests: properties, SAS URL, upload, copy, metadata,
      undelete, incl. an `AllowMutations=false → 403` check on each mutation). Each required first
      extracting the target handler's body from an inline lambda into a named `internal static`
      method (the pattern `ApiClientEndpointsPreviewTests.cs` already proved), touching only the
      specific handlers tested. Also closed a real, live gap found while doing this pass (not
      originally scoped, folded into §3.5 below): AKS/Redis/Service Bus/Storage's "test connection"
      endpoints returned `ex.Message` verbatim in a 200 response — a live secret-leak path, since
      these SDKs' exceptions frequently embed the connection string/kubeconfig path — fixed via a
      shared `ConnectionTestError.Describe(ex)` classifier plus server-side logging
      (`ConnectionTestErrorTests.cs`, 6 tests, incl. a regression test proving a kubeconfig path
      never reaches the response body). Closed out the rest of the endpoint list too:
      `AgentEndpointsTests.cs` (4 tests: chat/clear/status, incl. the empty-message 400 never calling
      the model client), `MonitoringEndpointsTests.cs` (8 tests: rule CRUD — the SSE `/stream`
      endpoint is deliberately untested, its raw `HttpContext`-response/`PeriodicTimer` loop doesn't
      fit the extract-and-assert-on-`IResult` pattern without a much heavier fake `HttpContext` than
      warranted), `ConfigEndpointsTests.cs` (3 tests: export/import round trip via `AppDataSandbox`),
      and — the single most valuable test added this pass —
      `ConfigCollectionsCredentialSecretTests.cs` (3 tests, the §3.6 regression test test-plan.md
      specifically calls for): `PUT /api/config/collections` was moved out of `Program.cs` into
      `ConfigEndpoints.SaveCollectionsAsync` (a small, targeted slice of §3.4 — not the full
      "move every config endpoint out of Program.cs," just this one handler, done specifically to
      make the regression test possible) along with the `StripCredentialSecrets`/`StripNode`/
      `StripAuth` helpers it calls; the tests prove a populated `CredentialSecret` at the collection
      level and at a deeply-nested folder/request level is stripped before ever reaching disk
      (reloaded via a fresh `CollectionRepository` to prove it), plus that non-secret auth fields
      survive the strip. `SidecarAuthHeaderBuilderTests.cs` (18 tests) directly covers the
      security-sensitive Bearer/API-key/Basic/OAuth2 precedence logic — verified the documented
      precedence exactly, no bug found. `SidecarCredentialStoreTests.cs` (11 tests, bonus) exercises
      the real OS-backed keychain in this environment. 32 → **147** `SwebKit.Sidecar.Tests` passing.
      One test-infrastructure bug found and fixed along the way: `AppDataSandbox` mutated a
      process-wide env var with no synchronization — safe with one test class using it, a real race
      once more classes did (xUnit runs test classes in parallel by default), corrupting an unrelated
      test class; fixed with a static `SemaphoreSlim` gate (not `lock`, since async tests can resume
      on a different thread). `DemoModeServiceTests.cs` (9 tests) closes the last endpoint-adjacent
      gap: demo namespace shape, per-namespace client caching, the unknown-namespace error path,
      demo Redis/Storage config resolution. `SystemEndpointsTests.cs` (3 tests) and 8 new tests added
      to `ConfigEndpointsTests.cs` cover the endpoints moved out of `Program.cs` for §3.4 (the demo
      overlay on `GET /api/config/profiles`, that the returned profile data is a real clone rather
      than a reference back into the live repository, the demo-collection prepend on both
      collections endpoints, and round-trip persistence for environments/user-settings/profile
      saves). 32 → **167** `SwebKit.Sidecar.Tests` passing — this effectively completes
      test-plan.md §1. Two items remain deliberately unaddressed, both
      documented with reasoning rather than left as silent gaps: `MonitoringEndpoints.cs`'s SSE
      `/stream` endpoint (its raw `HttpContext`-response/`PeriodicTimer` loop doesn't fit the
      extract-and-assert-on-`IResult` pattern without a much heavier fake `HttpContext`), and a
      `Program.cs` `WebApplicationFactory` integration smoke test (`Program.cs` calls
      `builder.WebHost.UseUrls` with a hardcoded fallback port — a known sharp edge where
      `WebApplicationFactory` can force a real Kestrel bind instead of the in-memory `TestServer`;
      in this shared dev environment, where a real sidecar instance is often already running on that
      port, that risks a flaky port collision for a test whose value — exception-handler status-code
      mapping, CORS — is simple, inspectable code already indirectly covered by the e2e suite).
- [x] CI wiring (test-plan.md Module 7) — done: Vitest now runs in the `frontend` job, a new `rust`
      job runs `cargo clippy`/`cargo test` gated on `src-tauri/**` changes.
- [x] Frontend architecture decomposition (technical-plan.md Module 2) — **done**: `lib/hooks.ts`
      (1540 lines, every domain's hooks in one file) split into
      `web/src/lib/hooks/{useServiceBus,useAks,useRedis,useStorage,useApiClient,useAgent,
      useMonitoring,useProfile,useCommandPalette}.ts` with `index.ts` re-exporting all of them, so
      the ~400 existing `import { useX } from "@/lib/hooks"` call sites needed zero changes.
      `RedisPage.tsx` (1330 lines, 31 `useState`), `StoragePage.tsx` (975 lines, ~30 `useState`), and
      `ApiClientPage.tsx` (916 lines, 15+ `useState`) all decomposed into a
      `<Feature>PageContext`/`use<Feature>PageContext` hook plus per-tab/per-view components,
      mirroring `AksWorkspaceContext.tsx`/`AksPage.tsx`'s proven shape: `RedisPage.tsx` 1330 → 149
      lines, `StoragePage.tsx` 975 → 98 lines, `ApiClientPage.tsx` 916 → 235 lines.
      `BlobRecoveryPanel.tsx`'s previously-partial context extraction is now finished (§2.2 called
      this out explicitly — "don't leave it half-done"). `RequestEditor.tsx` (937 lines) was
      deliberately left untouched — it only has 5 `useState` calls and already delegates
      GraphQL/WebSocket to their own panels, so its length is straightforward JSX, not the
      state-monolith problem this decomposition targets. `ApiClientPage.tsx`'s built chunk is
      482.73kB (151.73kB gzip), under Vite's 500kB warning threshold, so no `React.lazy` split was
      needed.
      **Note for whoever picks up Module 5's remaining `React.memo` pass**: neither
      `RedisPageContext.tsx` nor `StoragePageContext.tsx` wraps its context `value` object in
      `useMemo` (unlike `AksWorkspaceContext.tsx`), and most of their handlers aren't wrapped in
      `useCallback` either — so `React.memo` on a context-consuming child wouldn't yet see a stable
      prop/context reference to skip a re-render against. But this is *not* a quick fix: a single
      shared context value (the pattern all three of these files use, matching the AKS reference)
      means one context field changing (e.g. a keystroke in a rename input) still changes the whole
      `value` object identity even after memoizing it, so every consumer re-renders regardless —
      `useMemo`/`useCallback` alone would mostly add bookkeeping overhead without the payoff a real
      fix needs. An actual win here means splitting each page's one big context into a few narrower
      ones (e.g. tree/list state vs. dialog state vs. selection), which is real design work, not a
      mechanical follow-up to this decomposition — scope it as its own pass rather than folding it in
      here.
- [x] Shared UI primitives (technical-plan.md Module 4) — done: `Dialog`, `EmptyState`, `Skeleton`,
      `QueryState` added under `web/src/components/shared/`; migrated onto in API Client dialogs,
      collection export dialog, AKS alert-rule dialog, keyboard-shortcuts panel; command palette and
      entity command palette given `role="dialog"`/`aria-modal` directly (kept their existing
      custom focus/Escape logic instead of a full migration).
- [x] Performance pass (technical-plan.md Module 5) — **partial**: §5.1 and §5.2 done —
      `service-bus/MessageList.tsx`, `RedisPage.tsx`'s key tree, and `StoragePage.tsx`'s blob list
      all now render through `@tanstack/react-virtual`, matching the pattern first proven in
      `CollectionTree.tsx`. Virtualizing `MessageList.tsx` required converting its real `<table>`
      into a CSS-grid layout (a shared `gridTemplateColumns` string on the sticky header and every
      absolutely-positioned row, with explicit ARIA table roles replacing the semantics native table
      elements gave for free) since `<tr>` can't be absolutely-positioned independently without
      losing shared column widths. §5.3 (systematic `React.memo` pass) is **not done** and, per the
      note under Module 2 above, isn't just a mechanical follow-up now that the decomposition
      landed — the one-big-context pattern these pages (and `AksWorkspaceContext.tsx`) use caups
      causes any single field change to re-render every consumer regardless of memoization, so a real win
      needs splitting each context into narrower ones first. §5.4 (command-palette registry
      consolidation) is **done**: `CommandPalette.tsx` and `EntityCommandPalette.tsx` now share
      `lib/paletteSearch.ts` (fuzzy scoring — the entity palette previously did plain substring
      matching), `lib/hooks/usePaletteNavigation.ts` (selected-index/scroll-into-view list nav +
      open-focus-reset lifecycle), and `components/layout/PaletteOverlay.tsx` (backdrop/dialog
      shell), so a future domain-scoped palette can reuse these instead of copy-pasting a third
      implementation. Each palette keeps its own item shape, keyboard semantics (MRU vs.
      entity/action drill-down), and existing testids unchanged.
- [x] Production/ops readiness (technical-plan.md Module 6) — **partial**: sidecar file logging via
      `FileLoggerProvider`/`AppBootstrap` (moved to `SwebKit.Core.Diagnostics` so both MAUI and the
      sidecar share it), sidecar 500s now log the real exception server-side while still returning a
      generic message to the client, and a footer "Reconnect" button now calls the pre-existing
      `restart_sidecar` Tauri command when `useHealth()` detects an outage. The Tauri capabilities
      file (§6.6) remains deliberately **skipped**: adding an explicit capabilities file risks
      silently breaking the IPC bridge (wrong permission scoping) and this pass had no way to verify
      it against the real packaged desktop app — do this with a live Tauri build to test against,
      not blind. §6.4 (packaging: identifier, signing, auto-update, changelog) is **not started and
      not something to guess at**: `tauri.conf.json`'s `identifier` is still the literal placeholder
      `com.companyname.swebkit`, there's no `tauri-plugin-updater`, and no `CHANGELOG.md`. A real
      reverse-DNS identifier is a one-way, org-identity decision (it's baked into the Windows
      installer's upgrade-detection registry key and would need to stay stable across every future
      release) that only the user/org can make — fabricating one here would just replace one
      placeholder with another. Needs an explicit answer from the user before any of §6.4 is
      actionable; code-signing further needs an actual certificate, which the plan already correctly
      scopes as "a procurement/ops task outside this repo's control."
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
      live forward so none survive as orphans after the app closes. **Pod shell exec is done**
      (2026-08-02, explicitly requested as a V1 blocker): `src-tauri/src/pod_shell.rs` spawns
      `kubectl exec -it pod/<pod> -n <ns> [-c <container>] [--context ...] -- sh -c "command -v
      bash && exec bash || exec sh"` through a real pty (`portable-pty`, ConPTY on Windows), with
      I/O crossing the Tauri IPC boundary base64-encoded in both directions. `PodShellPanel.tsx`
      (new `@xterm/xterm` + `@xterm/addon-fit` dependency) renders the terminal — deliberately not
      built on the shared `Dialog` primitive, since that closes on Escape and a real shell session
      needs Escape to reach the shell (e.g. vim). Wired into `PodsTab.tsx`'s context menu.
      **A real deadlock was caught and fixed while building this**, worth flagging for anyone
      touching this file: the original design detected "session ended" by reading the pty master
      until EOF in a background thread, which hangs forever on Windows/ConPTY — EOF is never
      signaled to a reader while this process still holds its own `master` handle open (needed for
      the session's whole lifetime, for resize support). Fixed by polling `Child::try_wait()` from
      a dedicated thread instead. A second, related deadlock was caught in review before shipping:
      a *blocking* `Child::wait()` in that thread would hold the child's `Mutex` for the whole
      session, making it impossible for `close_pod_shell`/`kill_all_pod_shells` to ever acquire the
      lock to call `kill()` — fixed by polling with a short sleep between checks instead of a
      blocking wait, so the lock is only held briefly. **Not validated by an automated test**: an
      attempt to write a Rust integration test driving a real pty round-trip found that even a
      trivial, zero-output child process (`cmd /C exit 7`) never reached `try_wait() == Some(_)`
      within 15 seconds when driven by a bare `Read`/`Write` harness with no real terminal emulator
      attached — pointing to a ConPTY console handshake requirement a full terminal emulator
      (xterm.js, in the real app) evidently satisfies but a minimal test harness doesn't. That test
      was removed rather than shipped half-working; see the doc comment left in place of it in
      `pod_shell.rs` for the full detail. **This means the feature has not been
      end-to-end verified against a real cluster** — the mutex/threading design is sound by code
      review, and the frontend wiring is verified (a new e2e test confirms the panel opens and
      reports "requires the desktop app" gracefully in the browser-mode fallback Playwright runs
      under), but someone needs to manually click through a real `kubectl exec` session in the
      packaged app against a real pod before this is fully trusted.
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
| `dotnet test tests/SwebKit.Sidecar.Tests` | 171 passed (22 existing + 6 connection-pool + 4 agent tool-calling + 11 AksEndpoints + 22 RedisEndpoints + 14 ServiceBusEndpoints + 19 StorageEndpoints + 6 ConnectionTestError + 4 AgentEndpoints + 8 MonitoringEndpoints + 11 ConfigEndpoints + 3 CredentialSecret regression + 18 SidecarAuthHeaderBuilder + 11 SidecarCredentialStore + 9 DemoModeService + 3 SystemEndpoints) |
| `dotnet test tests/SwebKit.Core.Tests` | 798 passed |
| `cargo clippy --all-targets -- -D warnings` (`src-tauri`) | Pass, 0 warnings |
| `cargo test --lib` (`src-tauri`) | 53 passed (50 existing + 3 port-forward parsing) |

### Known flaky test — investigated, not resolved

`service-bus.spec.ts`'s "copy body and copy full message buttons work" has intermittently failed
throughout this initiative (roughly 1-in-5 to 1-in-8 runs, confirmed via ~20 isolated repeated runs
on 2026-08-02). **Two things worth recording so a future attempt doesn't re-tread the same ground:**

1. **The failure is not what it first looks like.** The initial hypothesis — that
   `navigator.clipboard.writeText()` silently rejects because a freshly created
   `browser.newContext()`/`newPage()` lacks OS-level focus (a real, documented Chromium Clipboard
   API requirement), so the "Copied!" feedback never appears and the assertion times out — was
   tested by adding `page.bringToFront()` before each clipboard interaction. It did **not** reduce
   the failure rate. Captured full output from an actual failure shows the real error is `Test
   timeout of 60000ms exceeded` inside the test's own `finally` block (`setDemoMode(page, false)`
   failing with "Target page, context or browser has been closed"), meaning something *earlier* in
   the test body hangs for the full 60s test timeout, and the browser gets force-closed by Playwright
   itself before cleanup runs — this is a genuine intermittent hang somewhere in the test, not a
   fast assertion race. The `bringToFront()` change was reverted rather than left in as a
   non-fix with a misleading justifying comment.
2. **This is very likely also the root cause of every other "cascading failure starting at
   service-bus.spec.ts:159" seen throughout this whole initiative** (dashboard.spec.ts and other
   unrelated specs occasionally showed the same pattern): when this test hangs and its worker gets
   killed, Playwright respawns a worker, which re-imports `playwright.config.ts` — a module with a
   top-level side effect (`fs.rmSync(e2eAppDataRoot, ...)`) — while the previous worker's child
   processes (a spawned `dotnet` sidecar, in particular) haven't fully released their file handles
   yet, producing the familiar `EPERM` on `.e2e-appdata` for every subsequent test in that run. In
   other words: the dozens of "unrelated flake, retried and got a clean run" incidents logged across
   this initiative's commits were most likely all downstream of this one intermittent hang, not
   independent flakes. Confirmed via `Get-Process -Name dotnet` after a failed run: 9 stray `dotnet`
   processes were still resident, all spawned within the same few seconds.

Not fixed in this pass — pinpointing exactly which `await` hangs needs either instrumented tracing
or a dedicated, patient repro session (repeat-each within one worker doesn't give independent
samples, since one hang poisons every later repeat in the same worker — isolated
`npx playwright test` invocations, one at a time, with process cleanup between each, is the only way
to get real signal, and that's slow).

## Definition of Done

1. Every critical/high-severity bug found in `production-readiness-review.md` §3 is fixed and has a
   regression test.
2. Every feature-parity gap rated "Important" in §4 is closed or explicitly re-classified as an
   intentional substitution with a documented rationale.
3. ~~The three uncommitted-but-complete features (`monitoring-rebuild`, `api-client-git-completion`,
   `api-client-ux-overhaul`) are committed, manually verified in the real Tauri shell, and merged.~~
   **Dropped (2026-08-01)**: the branch holding this work no longer exists, confirmed by the user —
   not achievable, not a remaining gate on Definition of Done.
4. Crash telemetry, sidecar file logging, and sidecar crash recovery are all in place
   (`technical-plan.md` Module 6).
5. CI runs Vitest and Rust checks in addition to its current .NET/Playwright coverage.
6. The sidecar's endpoint/service test coverage matches the standard already proven on
   `ApiClientEndpointsPreviewTests.cs` for all 9 endpoint files.
7. `docs/architecture/architecture.md`/`design.md` describe the Tauri+React+sidecar system as
   primary, and all dangling doc references identified in this review are resolved.
8. A packaging decision (code signing, auto-update, app identifier) is made and implemented or
   explicitly deferred with a reason, not left as an unaddressed placeholder.
