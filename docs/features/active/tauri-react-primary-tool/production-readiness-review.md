# Production-Readiness Review — Tauri + React

This is the consolidated findings report behind [index.md](index.md). It was produced by six
parallel research passes: a synthesis of all existing planning docs, a code inventory of `web/src`,
a MAUI-vs-React feature-parity comparison, a sidecar (.NET) code-quality audit, a test-coverage and
packaging audit, and a hands-on click-through of the running app in demo mode. Findings are grouped
by domain; each links forward to the plan doc ([ux-plan.md](ux-plan.md),
[technical-plan.md](technical-plan.md), [test-plan.md](test-plan.md)) that turns it into scheduled
work.

**Date:** 2026-08-01

---

## 1. Executive summary — top blockers

Ranked by risk of shipping without fixing them, across all categories:

1. **React key-collision bug causes duplicate renders — and possibly duplicate side effects.**
   Confirmed via a live console error (`Encountered two children with the same key`, a
   `Date.now()`-shaped key) and reproduced independently in three unrelated features: API Client
   error messages, Monitoring alert rows, and AKS restart notifications rendered twice. This is a
   correctness bug, not a cosmetic one — it needs to be traced to confirm whether the underlying
   mutation (an AKS restart, a Service Bus resubmit) fires once or twice against real
   infrastructure. **New finding, highest priority in this review.** → [ux-plan.md](ux-plan.md) Phase 0.
2. **No crash/error telemetry anywhere in the new stack.** Zero Sentry/App Insights/equivalent in
   `web/src`, `src-tauri/src`, or `src-sidecar/`. Shipping with no visibility means field bugs are
   invisible until a user complains. → [technical-plan.md](technical-plan.md).
3. **Sidecar produces no persisted logs in production.** The MAUI app's `FileLoggerProvider`
   (structured NDJSON, daily files, 7-day retention, crash-safe emergency write) was never ported to
   `src-sidecar/Program.cs`, which uses only default console logging — discarded in a windowless
   release build. Combined with #2, a production crash leaves no trace at all. → [technical-plan.md](technical-plan.md).
4. **No sidecar crash recovery.** A `restart_sidecar` Tauri command exists but is called by nothing
   in the frontend — dead capability. No watchdog detects an unexpected child-process exit; a
   sidecar crash mid-session silently breaks the app until manual relaunch. → [technical-plan.md](technical-plan.md).
5. **Unsigned Windows installer, no auto-update, placeholder app identifier
   (`com.companyname.swebkit`).** All real blockers before any wider distribution, despite a
   `MICROSOFT_STORE_SUBMISSION_GUIDE.md` already existing in the repo suggesting Store submission is
   a near-term goal. → [technical-plan.md](technical-plan.md).
6. **Sidecar has ~3,000 lines of endpoints/services with test coverage on one file.** 8 of 9
   endpoint files and 5 of 6 services — including the two most operationally risky
   (`AksEndpoints.cs`, mutating cluster state; `RedisEndpoints.cs`, mutating keys) — have zero test
   coverage, despite the test harness for it already existing and proven on one file. → [test-plan.md](test-plan.md).
7. **Agent tool-calling is completely missing in the sidecar**, while `SwebKit.Agents` already has 9
   working tool implementations (5 AKS, 2 Service Bus, 2 Observability) wired into MAUI.
   `SidecarAgentChatService` hard-codes *"No tool calling is available in the sidecar mode."* This is
   additive work, not a rearchitecture — the tools already call `IAksClient`/`IServiceBusClient`,
   both fully wired into the sidecar. → [ux-plan.md](ux-plan.md).
8. **CI doesn't run the Vitest suite or any Rust checks.** `build.yml`'s `frontend` job only
   typechecks and builds; the 8 Vitest files never execute in CI, and there's no `cargo
   test`/`cargo clippy` job for `src-tauri`. Regressions in either area are undetected until manual
   testing. → [test-plan.md](test-plan.md).
9. **Real work is sitting uncommitted.** `api-client-git-completion` and `api-client-ux-overhaul`
   are both `Review` status but implemented on an uncommitted branch
   (`feat/api-client-ux-and-git`); `monitoring-rebuild` is `Done` but also uncommitted. This is the
   single fastest win available — land what's already built before scheduling new work.
10. **Several previously-flagged critical issues are already fixed** and should be struck from any
    plan built on the older docs: plaintext auth-secret storage (now OS-keychain-backed with an
    explicit pre-save null-out), Storage upload/copy/metadata/versions/SAS routes (all wired),
    Redis keyspace-health/prefix-memory/hash-mutations (all wired), Gateway API grids, CollectionTree
    virtualization, command-palette fuzzy search. Do not re-plan work that's done — see §2.

---

## 2. Documentation hygiene — fix before planning further work on top of it

The existing planning corpus is large (megaplan, 3 review/plan docs, 11 active feature folders,
5 pitfalls docs, an architecture folder) and has drifted from the code in specific, checkable ways:

| Issue | Detail | Action |
|---|---|---|
| **Dangling doc references** | Six feature folders are cited by relative link from `docs/features/README.md`, `monitoring-rebuild/*`, and `post-migration-ux-review/*` but don't exist under `active/` or `archive/`: `demo-mode-parity`, `aks-migration-fixes`, `service-bus-migration-fixes`, `dashboard-redesign`, `bruno-import-and-reorder`, `agent-multimodel-api-client`, `tauri-security-hardening`. This violates the repo's own traceability contract. The critical "Observability/DevOps permanently dropped" decision is sourced to one of these (`demo-mode-parity/index.md`) — corroborated elsewhere, but its primary source is gone. | Recreate a minimal `demo-mode-parity/index.md` (or fold its one decision into `docs/features/README.md` directly) so the traceability chain resolves; remove or update the other five dangling links. |
| **`docs/features/README.md`'s "Canonical Feature Order"** | Points to `docs/features/foundation-mvp/`, `service-bus/`, `aks/`, `polish-advanced/` — **none exist**. Actual structure is `active/` + `archive/`. | Rewrite this section to reflect the real `active/`/`archive/` structure. |
| **`docs/architecture/architecture.md` and `design.md` describe only MAUI** | Both present MAUI (`SwebKit.App`, `MauiProgram.cs`) as if it were the sole/current architecture, while sibling `codebase-guide.md` already routes to `web/src/components/...` and `src-tauri/src/git.rs`. An agent reading these three "required preload" docs today gets an internally inconsistent picture of which stack is canonical. | Rewrite `architecture.md`/`design.md` for the Tauri+React+sidecar system as primary, MAUI as legacy/reference. Tracked as a technical-plan task. |
| **`docs/plans/test-coverage-expansion.md` is stale** | States a baseline of "35 e2e tests, zero unit tests, zero sidecar tests." Actual: ~191 e2e tests, 116 Vitest unit tests, a real (if thin) sidecar test project. | Re-baseline before using its phase list; see [test-plan.md](test-plan.md). |
| **`docs/plans/codebase-review-2026-07-18/*` (2 files, 1200+ lines) are 100% MAUI-scoped** | Never mention `web/`, `src-tauri/`, or `src-sidecar/`. | Treat as historical/MAUI-only; do not use as a source for the new stack's technical plan except where they describe issues in shared `SwebKit.Core/.Azure/.Kubernetes` libraries the sidecar still depends on (kubectl `ArgumentList` hardening, `DefaultAzureCredential` factory bypass, `WindowsCredentialStore` exception swallowing — these are live risk and are carried into [technical-plan.md](technical-plan.md)). |
| **`docs/environment-variables-redesign.md`** | 555-line MAUI/Blazor-only design doc (`.razor` component tree) dated 2025-06-28, "Ready for implementation" — appears never built for MAUI and superseded by the much narrower, already-shipped `api-client-key-vault` feature. | Archive; do not treat as a live requirements source. |
| **`docs/packaging-and-install.md` is MAUI/MSIX-only** | Points to `scripts/README.md` + `scripts/tauri/build-msi.ps1` for the Tauri installer story, which wasn't independently verified in this pass. | Verify those scripts exist and write a Tauri-equivalent packaging doc (see [technical-plan.md](technical-plan.md)). |
| **List virtualization status is contradicted across docs** | `feature-parity-ui-improvements.md` and `code-review-quality.md` both still list CollectionTree virtualization as an open gap; it was in fact implemented (commit `bd0bcff`). | Both docs stale on this point; `technical-plan.md`'s virtualization tasks are scoped against the *current* code (§4 below), not these docs. |
| **`docs/architecture/functionalities/agent.md`** | Documents the MAUI tool-calling architecture as if current for the sidecar, which has none. | Update once agent tool-calling lands (see [ux-plan.md](ux-plan.md)). |

---

## 3. Functional bugs found in the running app (hands-on review, demo mode)

These were found by actually clicking through the app, not by reading code — several are confirmed
via live browser console errors, not just visual impressions.

| # | Bug | Evidence | Severity |
|---|---|---|---|
| 1 | **Duplicate list-rendering from a React key collision.** Console: *"Encountered two children with the same key... 1785571152330.5215"* — a timestamp-shaped key that isn't guaranteed unique. Reproduced in: API Client error messages (rendered twice), Monitoring alert rows (rendered twice), AKS restart notification history (two identical entries for one click). | Live console error | **Critical** — may indicate the underlying mutation fires twice, not just the UI |
| 2 | **Invalid nested `<button>` in Service Bus entity tree.** React's own hydration-error console warning: *"In HTML, `<button>` cannot be a descendant of `<button>`."* The entity row is a button containing two more buttons (active/DLQ count badges). | Live console error | High — unpredictable click-target behavior |
| 3 | **AKS "Not configured" vs. "Connected" shown simultaneously** on Dashboard, Settings, and the footer status bar, while the AKS page itself works fully with demo data. Root cause: AKS connection fields are inert in demo mode, but nothing else communicates "demo-configured" the way Service Bus/Redis/Storage do. | Visual, cross-page | High — actively confusing, looks broken |
| 4 | **API Client demo/bundled request targets the wrong port** (`5198` instead of the real sidecar port `5199`) — the first request a new user is likely to click fails out of the box. | Reproduced | High — bad first impression |
| 5 | **API Client collections/environments cluttered with ~24 leftover dev/e2e-test artifacts** ("Test Environment," "Tab Test Collection," "E2E Collection," etc.) alongside real demo content, with no search/filter in either picker. Demo Mode doesn't inject clean, isolated data here the way it does for Service Bus/Redis/Storage — it just renders whatever's in the persisted store. | Reproduced | High — breaks the demo/onboarding experience |
| 6 | **Raw, unlocalized backend exception text shown to the user** in API Client — e.g. a French OS socket error (*"Aucune connexion n'a pu être établie..."*) and a raw .NET `HttpClient` exception message for a malformed URL. | Reproduced | Medium — unprofessional, not actionable for the user |
| 7 | **API Client response panel silently clips at narrow widths.** At 700px viewport width, the response panel's content is 972px wide inside a 476px container with `overflow: hidden` and no scrollbar — ~496px is completely inaccessible. Not present at 900px. | Measured via computed styles | Medium — real breakage on smaller windows, distinct from the already-tracked "default proportions on wide windows" work |
| 8 | **AKS Pod YAML viewer shows Deployment-shaped content for a Pod** — `replicas`, `selector.matchLabels`, `template:` fields on a Pod object, and lowercase `kind: pod` (real Kubernetes uses `Pod`). Demo data generation reused Deployment YAML for Pod YAML. | Reproduced | Medium — misleading for a debugging tool specifically |
| 9 | **Redis key-tree rows are keyboard-inaccessible** — plain `<div>`s with `cursor-pointer` but no `role`/`tabindex`; Tab won't focus them, Enter/Space won't activate them. | DOM inspection | Medium (accessibility) |
| 10 | **No `role="dialog"`/`aria-modal` on any overlay** — command palette, Service Bus entity-search palette, keyboard shortcuts panel all verified `null` on both attributes. Screen readers won't announce them as modals. | DOM inspection, systemic | Medium (accessibility) |
| 11 | **Light-theme contrast failures on AKS pod status text** — measured 2.09:1 (green "Running") and 3.10:1 (red error states) against a 4.5:1 WCAG AA requirement for normal text. | Measured contrast ratio | Medium (accessibility/compliance) |
| 12 | **Redis Ops/Slow Log durations shown as raw `TimeSpan` strings** (`00:00:00.0483000`) instead of a human value (`48.3ms`), in two separate tabs. | Reproduced | Low — looks unfinished |
| 13 | **AKS "Services" buried in a secondary "Network" dropdown** while less-common resources (Jobs, CronJobs, HPA) get top-level tabs. | Visual | Low (discoverability) |
| 14 | **Storage uses emoji file/folder icons** (📄/📁) instead of the lucide-react icon set used everywhere else; inconsistent date formats across pages (`DD/MM/YYYY` in Storage/Monitoring vs. ISO in AKS logs/CSV content). | Visual | Low (consistency polish) |
| 15 | **Monitoring alert history shows raw severity `0`** for one row instead of a label — a visible symptom of Monitoring's already-tracked "entirely a mockup, no real backend persistence" root issue (`post-migration-ux-review` #1). | Reproduced | Low (symptom of a tracked issue, not new) |

Findings #1–2, #4–9, #12–14 are **new** — not present in any of the five existing UX-tracking docs
(`post-migration-ux-review`, `ux-followup-july-27`, `react-polish-aug-01`, `api-client-ux-overhaul`,
`aks-ux-improvements`). #3 and #10–11 touch areas those docs mention adjacently but not with this
specific detail. #15 is a known symptom of an already-tracked root cause.

---

## 4. Feature-parity gaps (MAUI vs. Tauri+React)

Corrected against the current code (several previously-tracked gaps are already closed — see §1
item 10 and the table below). Full detail and file references in the research agent's report;
consolidated here:

| Feature area | Capability | Gap severity | Status |
|---|---|---|---|
| Agent | Tool-calling (query pods/logs/queue stats from chat) | **Important** | Missing — `SidecarAgentChatService` explicitly disables it; 9 tool impls already exist in `SwebKit.Agents` and call already-wired clients |
| AKS | Open shell in pod (`kubectl exec`) | **Important** | Missing — context menu item hard-coded `disabled: true`; not architecturally hard (same shell-out pattern as `git.rs`/`native.rs`) |
| AKS | Real port-forward subprocess | **Important** | Partial — session registry exists, comment admits *"In production, this would spawn kubectl port-forward"*; no actual forwarding happens |
| AKS | Network Policy / PDB / Placement Constraints panels | Nice-to-have | Missing |
| Storage | Bulk download as a real ZIP (Service Bus already has this pattern) | Nice-to-have | Partial — sequential single-file downloads, not bundled |
| Redis | Live Pub/Sub message streaming + publish | Nice-to-have (by design) | Partial — honest read-only snapshot with a clear "not supported" notice, resolves the prior "wired to nothing" danger |
| API Client | GraphQL subscriptions over WebSocket | Nice-to-have | Partial — backend service exists, no frontend UI |
| Shell | Per-area live status dots, resource-aware command palette | Nice-to-have | Missing/partial |
| Settings | Dead `DevOpsSettings` tab (DevOps was dropped) | **Quick win, cleanup** | Should be deleted, not ported |
| Service Bus | Bulk/multi-select DLQ actions | Nice-to-have | Missing in both apps — net-new opportunity, not a regression |

**Already resolved, do not re-plan**: plaintext auth secrets, Storage upload/copy/metadata/SAS/versions,
Redis keyspace-health/prefix-memory/hash-mutations, command-palette fuzzy search, Gateway API grids,
pods CPU/Memory bars, dashboard pinning, Capture Rules, YAML apply, blob version diff, multi-pod log
correlation, Git multi-repo picker.

**Intentionally dropped, not gaps**: Observability, DevOps/Pipelines/Releases, Incident Timeline
(all confirmed removed from `SwebKit.Core.Abstractions` usage in the sidecar), Windows-specific
credential vault (superseded by a more portable cross-platform keyring), warmup-cache interfaces
(no MAUI-style cold-start problem in the sidecar model).

---

## 5. Security

| Finding | Detail | Severity |
|---|---|---|
| No authentication on the sidecar HTTP API | Any local process on the machine can call every endpoint — including AKS restart/scale/delete, Service Bus purge/resubmit, Redis/Storage mutations — once it discovers the (dynamically-assigned, in production) port. CORS restricts *browser* origins only, not other local processes. | Medium (single-user desktop app, but the mutation surface is real) |
| `AuthConfig.CredentialSecret` persistence guard is conditional, not structural | Relies on `JsonIgnoreCondition.WhenWritingNull` (i.e., "nothing sets this before save") rather than an absolute exclusion or a save-only DTO that can't carry the field. No current code path populates it before a `collections.json` write, but nothing structurally prevents a future one from doing so. | Medium |
| Raw exception messages returned to the frontend | Both the sidecar's global exception handler and several per-endpoint `catch` blocks serialize `ex.Message` verbatim — can include connection strings, file paths, or SDK-internal detail from Azure/K8s/Redis clients. | Medium |
| `AksEndpoints.cs` `/httproutes` swallows all exceptions, returns an empty array | A permission or connectivity failure is indistinguishable from "no HTTPRoutes exist" — actively misleading for a debugging tool. | Medium |
| Static mutable `IAksClient` singleton bypasses DI | `private static IAksClient? _client` field on the endpoint class — not mockable, can't be swapped on kubeconfig change without a restart, inconsistent with the proper per-alias `ConcurrentDictionary` pattern already used by `SidecarMonitoringConnectionPool`. | Medium (reliability, adjacent to security) |
| Carried forward from shared libraries (MAUI-era review, still live since the sidecar reuses these unchanged) | kubectl `ArgumentList` hardening in `SwebKit.Kubernetes`, `DefaultAzureCredential` factory bypass risk, `WindowsCredentialStore` silent exception swallowing. | See original `codebase-review-2026-07-18` items; re-verify still-applicable ones against current `SwebKit.Core`/`.Azure`/`.Kubernetes` before scheduling |

No secret values found logged anywhere (`ICredentialStore`/Key Vault/auth-header code all log keys,
never values). CORS is already correctly scoped to Tauri/localhost origins (fixed since the last
written review). Full task breakdown in [technical-plan.md](technical-plan.md).

---

## 6. Production / packaging / operational readiness

| Concern | Current state | Production-ready? |
|---|---|---|
| Crash/error telemetry | None anywhere in the new stack | **No** |
| Sidecar production logging | Default console logging only; MAUI's `FileLoggerProvider` never ported | **No** |
| Sidecar crash recovery | `restart_sidecar` command exists, called by nothing; no watchdog | **No** |
| Code signing | No `certificateThumbprint` or equivalent in `tauri.conf.json` | **No** |
| Auto-update | No updater plugin configured, no dependency in `Cargo.toml` | **No** |
| App identifier | `com.companyname.swebkit` — the Tauri scaffold's literal placeholder | **No** |
| Version | `0.1.0` | Expected pre-1.0 |
| Bundle targets | msi/nsis via `release.yml` on tag push, publishes a **draft** GitHub Release | Partial |
| Icons | Full icon set present | Yes |
| CHANGELOG / release notes | None; draft releases have just a tag name | **No** |
| CI coverage | `build.yml` runs dotnet build+test (Core/Azure/Kubernetes/DevOps/Agents, MAUI, sidecar) and Playwright e2e; does **not** run Vitest or any Rust check (`cargo test`/`clippy`) | Partial |
| Rust shell tests | `git.rs` (1291 lines, 15 commands) has zero dedicated tests | **No** |

Detailed task breakdown in [technical-plan.md](technical-plan.md) (packaging/ops items) and
[test-plan.md](test-plan.md) (CI items).

---

## 7. Code quality & architecture (summary — full detail in technical-plan.md)

- `RedisPage.tsx` (1255 lines, 31 `useState`) is now the largest, most monolithic file in the app —
  not yet on anyone's remediation radar, worse than the previously-flagged `ApiClientPage.tsx`
  (916 lines, unchanged since it was flagged) and `RequestEditor.tsx` (937 lines, unchanged).
  `StoragePage.tsx` (914 lines, 31 `useState`) has the same shape.
- `React.memo` used in exactly one place app-wide (`ResourceTable.tsx`); `@tanstack/react-virtual`
  used in exactly one place (`CollectionTree.tsx`) despite `MessageList.tsx` (829 lines), Redis's key
  tree, and Storage's blob list all being unvirtualized candidate lists.
- No shared `Dialog`/`Table`/`Skeleton`/`EmptyState` primitives — every feature hand-rolls
  loading/error/empty markup and modal chrome.
- `lib/hooks.ts` is a single 1540-line file covering every domain's React Query hooks.
- Two independent command-palette implementations with no shared registry.
- Sidecar: inconsistent demo-mode branching per endpoint file, inline config endpoints in
  `Program.cs` (inconsistent with every other domain's extension-method pattern), no cancellation-
  token propagation outside AKS endpoints, no OpenAPI surface, no request validation on most
  endpoints, duplicated auth-header-builder logic between MAUI and sidecar.
- TypeScript hygiene is a genuine strength: `strict: true`, `noUnusedLocals`/`noUnusedParameters`,
  only 2 `any` occurrences repo-wide, zero `@ts-ignore`. Dependencies are current-generation and lean.

Full ranked task list in [technical-plan.md](technical-plan.md).

## 8. Test coverage (summary — full detail in test-plan.md)

- E2E (Playwright): 22 spec files, ~178 tests, uneven — deep on api-client/service-bus/redis/storage,
  thin on settings/agent/monitoring/navigation (1–8 tests each).
- Unit (Vitest): 9 files, pure-logic only by deliberate design (no component rendering tier).
  `hooks.ts` (1540 lines), `tauri-bridge.ts`, and `api.ts` — the three largest/most central `lib/`
  modules — have no test coverage at all.
- Sidecar (xUnit): 3 files for ~3,000 lines of endpoints/services; 8 of 9 endpoint files and 5 of 6
  services untested, including the two riskiest (`AksEndpoints.cs`, `RedisEndpoints.cs`).
- No accessibility test tier (`axe-core` or equivalent) exists anywhere.
- CI runs .NET tests and Playwright, but not Vitest or any Rust check.

Full ranked task list in [test-plan.md](test-plan.md).
