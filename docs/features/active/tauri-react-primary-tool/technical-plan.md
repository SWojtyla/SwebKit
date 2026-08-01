# Technical Plan — Clean Code, Patterns, Performance, Production Readiness

Companion to [production-readiness-review.md](production-readiness-review.md). Covers the
code-behind work: architecture, patterns, performance, security, and operational readiness. Test
coverage has its own document, [test-plan.md](test-plan.md), since it's substantial enough to track
separately — reference it for anything test-shaped.

Touches `web/src`, `src-sidecar/`, `src-tauri/src`, `src/SwebKit.Azure` and `.Kubernetes` (narrowly,
where a shared-library bug is live risk for the sidecar), and `docs/architecture/`.

---

## Module 1 — Documentation hygiene (do this first; other agents/contributors read these docs)

### 1.1 Resolve dangling doc references

Six feature folders are cited by relative link but don't exist: `demo-mode-parity`,
`aks-migration-fixes`, `service-bus-migration-fixes`, `dashboard-redesign`,
`bruno-import-and-reorder`, `agent-multimodel-api-client`, `tauri-security-hardening`.

- Recreate a minimal `docs/features/archive/demo-mode-parity/index.md` (status: Archived) capturing
  just the one load-bearing decision everything else cites: Observability and DevOps/Pipelines are
  permanently dropped from the Tauri+React rewrite (2026-07-26). This satisfies the traceability
  contract in `docs/features/README.md` without reconstructing a whole feature's history.
- For the other five: grep every reference to each name across `docs/`, and either (a) fold the
  cited content into the referencing doc directly and remove the link, or (b) if the work is real and
  still relevant, create a stub `index.md` under `archive/` with status `Archived` and a one-line
  "superseded by X" note. Do not spend time reconstructing full history for these — the goal is a
  resolvable link, not a rewritten feature doc.

### 1.2 Fix `docs/features/README.md`'s stale "Canonical Feature Order"

Replace the four nonexistent folder references (`foundation-mvp/`, `service-bus/`, `aks/`,
`polish-advanced/`) with the actual `active/`/`archive/` structure, and add this feature
(`tauri-react-primary-tool`) as the top-level entry point for finding current priorities.

### 1.3 Rewrite `docs/architecture/architecture.md` and `docs/architecture/design.md` for the Tauri+React system as primary

Both currently describe only the MAUI system as if it were canonical (MAUI sequence diagrams,
`MauiProgram.cs`, `SwebKit.App` component tree), while sibling `docs/architecture/codebase-guide.md`
is already partially React-aware. An agent or new contributor reading the three "required preload"
architecture docs today gets an internally inconsistent picture.

- Rewrite `architecture.md` to describe: Tauri shell (`src-tauri/`) ↔ sidecar (`src-sidecar/`, ASP.NET
  Minimal API) ↔ React frontend (`web/`), with the sidecar-reuses-`SwebKit.Core/.Azure/.Kubernetes/
  .Redis/.Agents`-unchanged relationship the megaplan established. Move the current MAUI-specific
  content to a clearly-labeled "Legacy MAUI architecture (reference only)" section or a separate
  `docs/architecture/maui-legacy.md`.
- Do the same for `design.md`.
- Carry forward the still-accurate structured-logging design described in `architecture.md`
  (`FileLoggerProvider`, NDJSON, daily files, 7-day retention) as the **target design** for the
  sidecar's logging (see Module 6.2) — it's a good design, it's just only wired into MAUI today.

### 1.4 Re-baseline `docs/plans/test-coverage-expansion.md`

Its "Current State" section (35 e2e tests, zero unit tests, zero sidecar tests) is significantly
stale. Update it to reflect actual current counts (see [test-plan.md](test-plan.md) for the numbers)
before anyone plans further test work from its phase list, or fold its still-relevant phases
(component-test-tier decision, sidecar integration tests, accessibility/perf) directly into
[test-plan.md](test-plan.md) and mark the old doc `Archived` with a pointer.

### 1.5 Scope-limit or archive MAUI-only review docs

`docs/plans/codebase-review-2026-07-18/Deep_Dive_Implementation_Plan.md` and
`Review_and_Improvement_Plan.md` are entirely MAUI-scoped. Add a header note to both stating they
apply to the legacy MAUI codebase only, with a pointer to this plan for the current stack, rather
than archiving outright (they remain useful if MAUI work is ever needed during the deprecation
window). Carry forward only the items that touch shared libraries the sidecar still depends on:
kubectl `ArgumentList` hardening (`SwebKit.Kubernetes`), `DefaultAzureCredential` factory bypass
(`SwebKit.Azure`), `WindowsCredentialStore` silent exception swallowing — verify each is still
present in the current code and track as Module 3 tasks below if so.

### 1.6 Archive `docs/environment-variables-redesign.md`

Superseded by the shipped `api-client-key-vault` feature; move to `docs/features/archive/` or mark
`Archived` in place with a note pointing to `api-client-key-vault`.

---

## Module 2 — Frontend architecture decomposition

The core problem across all four files below: a single component owns page-level layout, all
sub-feature state (via many `useState` hooks), all data-fetching orchestration, and all sub-view
rendering. This makes `React.memo` on child components ineffective (see Module 5), makes the file
hard to review/modify safely, and is why the same monolithic shape has now appeared twice more
(`RedisPage.tsx`, `StoragePage.tsx`) since it was first flagged for `ApiClientPage.tsx`.

**Standard remediation pattern** (apply consistently across all four): extract a
`<Feature>PageContext` + `use<Feature>PageState` hook that owns the cross-cutting state (selection,
tab, dialog-open flags), then extract each tab/sub-view into its own component that reads from
context instead of receiving 10+ props. This is the same pattern `AksWorkspaceContext.tsx` already
demonstrates successfully for the AKS feature (602 lines serving 29 components) — use it as the
reference implementation, not a green-field design.

### 2.1 `redis/RedisPage.tsx` (1255 lines, 31 `useState`) — now the largest file in the app

- Extract `RedisPageContext`/`useRedisPageState` covering: selected key, active tab (Keys/Server
  Info/Slow Log/Keyspace/Prefixes/Ops/Pub-Sub), search/filter state, dialog-open flags.
- Extract each of the 7 tabs into its own component under a new `redis/tabs/` directory (mirroring
  `aks/`'s per-resource-tab structure), each consuming context instead of props.
- Target: `RedisPage.tsx` itself shrinks to layout/tab-switching composition (~150-250 lines, matching
  `AksPage.tsx`'s 379 lines for a comparably broad feature).

### 2.2 `storage/StoragePage.tsx` (914 lines, 31 `useState`)

- Same treatment: `StoragePageContext`/`useStoragePageState` for selected container/blob, active
  view (Browser/Recovery), upload/copy/metadata dialog state.
- Extract Browser and Recovery into separate components (Recovery already has `BlobRecoveryPanel.tsx`
  partially extracted — finish the split, don't leave it half-done).

### 2.3 `api-client/ApiClientPage.tsx` (916 lines) and `RequestEditor.tsx` (937 lines)

- These were flagged in the prior architecture review (`docs/plans/react-architecture-performance-review.md`)
  and are **unchanged** since. Re-confirm the prior review's specific recommendation
  (`ApiClientContext` + `useApiClientState`, ~500 lines removed from the page) is still the right
  shape given what's landed since (Key Vault variables, virtualized CollectionTree, the
  uncommitted `api-client-ux-overhaul` layout work) — do this decomposition **after** Phase 1 of
  [ux-plan.md](ux-plan.md) lands the uncommitted branch, so it's not decomposing code that's about
  to change shape anyway.
- Note from `api-client-ux-overhaul`'s own status doc: its bundle chunk has grown to ~476kB,
  approaching Vite's 500kB warning threshold. If this decomposition doesn't naturally reduce it below
  threshold, add a `React.lazy` boundary around a heavy sub-panel (the CodeMirror-based body editor
  is the likely candidate) rather than accepting the warning.

### 2.4 `lib/hooks.ts` (1540 lines, every domain's React Query hooks in one file)

- Split into per-domain modules: `lib/hooks/useServiceBus.ts`, `useAks.ts`, `useRedis.ts`,
  `useStorage.ts`, `useApiClient.ts`, `useMonitoring.ts`, `useAgent.ts`, `useProfile.ts` (config/
  settings/profile CRUD), re-exported from a `lib/hooks/index.ts` barrel so existing `import { useX }
  from "@/lib/hooks"` call sites don't all need updating in the same change.
- Do this split incrementally, one domain at a time, verifying `tsc -b` and the relevant e2e specs
  pass after each domain's move — don't attempt it as one large mechanical change (400+ call sites
  make a single-PR full split high-risk for merge conflicts with the other phases in this plan
  running concurrently).

---

## Module 3 — Sidecar architecture & security

### 3.1 Replace the static mutable `IAksClient` singleton

`AksEndpoints.cs` has a `private static IAksClient? _client` field bypassing DI entirely — not
mockable, can't be swapped on kubeconfig change without a process restart. Replace with the same
per-alias `ConcurrentDictionary`-backed pattern `SidecarMonitoringConnectionPool.cs` already uses
successfully. This directly unblocks proper unit testing of `AksEndpoints.cs` (Module 3 of
[test-plan.md](test-plan.md) depends on this — a static singleton can't be substituted with a fake
in a handler-level unit test).

### 3.2 Fix `/httproutes` swallowing all exceptions

`AksEndpoints.cs`'s HTTPRoutes handler catches everything and returns an empty array, making a
permission/connectivity failure indistinguishable from "none exist." Let real errors propagate to
the (already-implemented) global exception handler like every other AKS endpoint does; keep a narrow
catch only for the specific "CRD not installed" case if that's the reason this pattern was added, and
return a distinguishable response for that case specifically.

### 3.3 Centralize demo-mode handling

Each endpoint file independently branches on `DemoModeService.IsDemoMode` in its own
resolve/create-client helper — five-plus parallel implementations of the same branch. Introduce a
single `IDemoAwareClientResolver<TClient>`-shaped abstraction (or a simpler shared extension method
if a generic interface is overkill) that every endpoint file calls the same way, and migrate each
endpoint file to it one at a time (this doubles as the fix needed for Phase 0.5 of
[ux-plan.md](ux-plan.md), which needs demo-mode "is configured" state to be consistent across every
surface — fix it once, here, structurally, rather than patching each surface's symptom separately).

### 3.4 Move config CRUD endpoints out of `Program.cs`

`Program.cs` still defines `/health`, `/api/demo-mode`, and all four `/api/config/*` endpoints
inline, inconsistent with every other domain's `Endpoints/*.cs` extension-method pattern. Extract to
`Endpoints/ConfigEndpoints.cs` (already exists — Health/DemoMode likely need a new
`Endpoints/SystemEndpoints.cs`) for consistency and testability.

### 3.5 Sanitize error messages before returning them to the client

The global exception handler and several per-endpoint `catch` blocks (test-connection endpoints,
`ApiClientEndpoints.execute`) serialize `ex.Message` verbatim — can leak connection strings, file
paths, SDK-internal detail. Map known exception types to safe, generic messages centrally in the
exception-handler middleware (already the right place, since it was added specifically to
centralize this kind of thing); keep the raw message in the server-side log only (once Module 6.2's
logging lands).

### 3.6 Make the `AuthConfig.CredentialSecret` persistence guard structural, not conditional

Currently relies on `JsonIgnoreCondition.WhenWritingNull` — i.e., safety depends on "nothing ever
populates this field before a save," not a structural guarantee. Introduce a save-only DTO for the
`PUT /api/config/collections` request path that simply has no `CredentialSecret`-shaped field at
all, so there's no field to accidentally serialize regardless of what any future code path does to
the in-memory `AuthConfig`.

### 3.7 Propagate `CancellationToken` consistently

AKS endpoints already accept and use `CancellationToken ct`; Redis, Storage, Service Bus, and
API-client endpoints don't, so a client disconnect can't cancel an in-flight long operation on those
paths. Add `CancellationToken` parameters and thread them through to the underlying client calls the
same way AKS already does — mechanical, low-risk, do it per-domain alongside other changes in that
file rather than as one sweeping change.

### 3.8 Add request validation

Most endpoints have no explicit validation beyond a handful of null/whitespace checks — malformed
GUIDs or invalid enum values currently surface as unhandled exceptions (then get sanitized per 3.5,
but still 500 instead of a clean 400). Add lightweight validation at the top of each handler for its
required fields (a shared minimal-API filter/attribute is a reasonable investment if there's enough
repetition once you look at all 9 endpoint files together; otherwise per-handler checks are fine
given the sidecar's small scale).

### 3.9 Consolidate duplicated auth-header-builder logic

`SidecarAuthHeaderBuilder.cs` (sidecar) and `src/SwebKit.App/Services/AuthHeaderBuilder.cs` (MAUI)
are two independent implementations of the same Bearer/API-key/Basic/OAuth2 header-building logic.
Once MAUI is far enough into deprecation that its `AuthHeaderBuilder` isn't being actively modified,
consolidate into a single `SwebKit.Core` implementation both apps call — until then, at minimum
leave a comment cross-referencing the two so a future auth-flow bug fix doesn't get applied to only
one.

### 3.10 Add an OpenAPI surface

No `AddEndpointsApiExplorer`/`AddSwaggerGen`/`MapOpenApi()` today — TypeScript types in `web/` are
hand-maintained against the C# DTOs with no generation or drift-detection. Add minimal OpenAPI
generation (ASP.NET's built-in `MapOpenApi()` is enough to start; a full Swagger UI is optional).
This is groundwork for a later type-generation pass (out of scope to actually generate TS types in
this plan, but the OpenAPI surface is a prerequisite worth landing now since it's cheap).

### 3.11 Re-verify shared-library issues carried from the MAUI-era review

Per Module 1.5: confirm whether kubectl `ArgumentList` hardening, `DefaultAzureCredential` factory
bypass, and `WindowsCredentialStore` exception-swallowing are still present in `SwebKit.Kubernetes`/
`.Azure`/`.Core` as described in `codebase-review-2026-07-18`. If still present, they're live risk
for the sidecar (which reuses these libraries unchanged) — schedule fixes; if already fixed, strike
from this plan.

---

## Module 4 — Shared UI primitives

`components/shared/` currently has only `ErrorBoundary.tsx` and `ConfirmBar.tsx` serving 9 feature
pages; every page hand-rolls loading/error/empty markup and modal chrome inline, inconsistently
(confirmed: 43 files use `isLoading`/`isPending` checks, only 2 use explicit `isError` checks, no
shared component behind any of them).

- **`<EmptyState>`**: icon/illustration slot, title, description, optional action button. Replace the
  ad hoc one-line "No X found" text scattered across Redis/Storage/Monitoring/etc. The Agent page's
  existing hand-built empty state (called out in the inventory as "nicely designed") is a reasonable
  starting visual reference.
- **`<Skeleton>`**: replace plain "Loading…" text with shimmer/placeholder blocks sized to the content
  they're replacing (table rows, cards). No shared skeleton exists today.
- **`<QueryState>`** (or equivalent thin wrapper): a component/hook that takes a React Query result
  and renders `<Skeleton>`/`<EmptyState>`/error/children consistently, so individual pages stop
  hand-rolling the `isLoading && ... ; error && ... ; data.length === 0 && ...` idiom independently
  43 times.
- **`<Dialog>`**: a real shared modal primitive with `role="dialog"`/`aria-modal` built in (see
  [ux-plan.md](ux-plan.md) Phase 4 — this is the structural fix, the UX plan's task is applying it).
  Every current ad hoc dialog (Alert Rule Dialog, Collection Export Dialog, Storage Copy dialog,
  every confirm dialog) should migrate to it over time, but don't block this module on migrating all
  of them — land the primitive, migrate opportunistically as each dialog is touched for other reasons.
- **`<Table>`** (generic, not AKS-specific): consider whether `aks/shared/ResourceTable.tsx` should
  become the app-wide generic table primitive (moved to `components/shared/`) rather than building a
  second one, since it's already memoized and reasonably generic (`<T extends {name, namespace?}>`).
  Evaluate this before building a new one from scratch.

---

## Module 5 — Performance

### 5.1 Virtualize `service-bus/MessageList.tsx` (829 lines)

Highest-leverage, lowest-risk item in this module: `@tanstack/react-virtual` is already a dependency
and already proven working in `CollectionTree.tsx` (same library, same repo, same patterns to copy).
Apply the same flatten-rows + `useVirtualizer` approach used there. Directly matters for a Service
Bus queue with a deep backlog — a realistic production scenario.

### 5.2 Virtualize Redis's key list/tree and Storage's blob list

Same risk class as 5.1 — currently unaddressed. Do these after 2.1/2.2's decomposition lands (easier
to virtualize a list that's already its own component than one still embedded in a 1000+ line page).

### 5.3 Systematic `React.memo` pass

`React.memo` is used in exactly one place app-wide (`ResourceTable.tsx`). Once Module 2's
decomposition extracts row/tab components with stable props (via `useMemo`/`useCallback` at the
parent, following the same pattern already fixed across all 14 AKS tab components in the prior Key
Vault PR's review-fix pass — reuse that exact pattern here), wrap the extracted list-row and
tab-view components in `React.memo`. Don't add `React.memo` to components still receiving fresh
inline props on every render — it's a no-op until the parent is memoized too (this exact mistake was
already found and fixed once for `ResourceTable`'s callers; don't repeat it for `MessageList`/Redis/
Storage row components).

### 5.4 Command-palette registry consolidation (performance-adjacent, mostly architectural)

Tracked in [ux-plan.md](ux-plan.md) Phase 6 as the user-facing outcome; the technical task is merging
`layout/CommandPalette.tsx` and `service-bus/EntityCommandPalette.tsx` onto one shared registry/hook
so a third bespoke implementation doesn't get built as more features get palette entries.

---

## Module 6 — Production / operational readiness

### 6.1 Crash/error telemetry

Add a telemetry SDK to all three layers:
- **`web/src`**: a browser-side error reporter (Sentry's React SDK or equivalent) wired into
  `ErrorBoundary.tsx`'s `componentDidCatch` (already has an `onError` prop slot per the inventory —
  use it) and a global `window.onerror`/`unhandledrejection` handler in `main.tsx`.
- **`src-tauri/src`**: a Rust-side panic hook (`std::panic::set_hook`) that reports to the same
  telemetry backend, or at minimum writes a structured crash file Tauri can pick up on next launch
  and forward.
- **`src-sidecar/`**: hook the same backend into the .NET logging pipeline (Module 6.2 covers the
  base logging; a `Sentry.AspNetCore`-style integration or equivalent then forwards errors from that
  pipeline).
- Decide on a provider before implementing (Sentry is the most common self-hostable/cloud option
  compatible with all three of React/Rust/.NET — evaluate against the team's existing tooling, e.g.
  the sidecar has no App Insights wiring at all today since Observability was dropped, so this is a
  fresh choice, not a continuation of anything).

### 6.2 Port structured file logging to the sidecar

`docs/architecture/architecture.md` already documents a good design (`FileLoggerProvider`, NDJSON,
daily per-feature files under `%APPDATA%/SwebKit/logs/`, 7-day retention, crash-safe emergency write,
redaction) — it's just only wired into `src/SwebKit.App/MauiProgram.cs` today.
`src-sidecar/Program.cs` has zero `builder.Logging.AddProvider(...)` call. Port the same provider
(it should be reusable as-is or with minimal change, since it likely already lives in
`SwebKit.Core`/a shared logging project — verify before rewriting) and wire it into the sidecar's
`Program.cs`. This is the single highest-leverage fix for "a real user's bug is diagnosable" —
pair with 6.1.

### 6.3 Sidecar crash recovery

- Wire up the existing-but-unused `restart_sidecar` Tauri command: add a watchdog in
  `src-tauri/src/sidecar.rs` that detects an unexpected child-process exit (distinct from a
  deliberate app-shutdown kill) and either auto-restarts or surfaces a clear "backend disconnected,
  click to reconnect" UI state to the frontend (the frontend already has a connection-status concept
  per the footer status dot — extend it to a "reconnect" action calling `restart_sidecar`).
- Route the sidecar's `eprintln!` stdout/stderr piping (already implemented) into the file logger
  from 6.2 instead of a console that doesn't exist in a windowless release build, so a sidecar crash
  during startup is still diagnosable.

### 6.4 Packaging: identifier, signing, auto-update, changelog

- Replace the placeholder `"identifier": "com.companyname.swebkit"` in `tauri.conf.json` with a real
  identifier before any wider distribution.
- Add Windows code-signing configuration (`bundle.windows.certificateThumbprint` or the CI-based
  signing step `release.yml` would need) — this requires a real code-signing certificate, which is a
  procurement/ops task outside this repo's control; scope the config-side work now so it's a
  drop-in once a cert exists.
- Add `tauri-plugin-updater` (Cargo dependency + `plugins.updater` config) so releases don't require
  manual reinstall. Verify `release.yml`'s draft-release flow is compatible with the updater's
  expected release-asset naming/manifest format.
- Add a `CHANGELOG.md` and update `release.yml` to populate real release notes (even a simple
  commit-log-since-last-tag auto-generation is better than a bare tag name) instead of publishing
  empty draft releases.
- Verify `scripts/README.md`/`scripts/tauri/build-msi.ps1` (referenced by `docs/packaging-and-install.md`
  but not independently confirmed in this research pass) actually exist and work; write a Tauri-
  equivalent packaging doc alongside the existing MAUI one once confirmed.

### 6.5 Rust-side testing and checks

`git.rs` (1291 lines, 15 commands) has zero dedicated tests. Add `cargo test` coverage for its pure
logic (diff parsing, status parsing, path-validation helpers) and add a `cargo clippy` + `cargo test`
job to CI (see [test-plan.md](test-plan.md) for the CI task) — currently neither runs anywhere.

### 6.6 Audit Tauri capabilities

No `src-tauri/capabilities/*.json` exists — the app relies on Tauri v2's implicit default capability
set rather than an explicit, reviewable permission grant. Add an explicit capabilities file scoping
exactly the shell/clipboard/dialog/fs permissions actually used, so the permission surface is
auditable from config rather than inferred from code.
