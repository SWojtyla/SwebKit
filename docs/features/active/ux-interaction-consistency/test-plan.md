# App-Wide UX & Interaction Consistency — Test Plan

## Scope

This plan touches `web/src/**` only (no backend/domain changes beyond threading an already-present
`error` through an existing hook). Test levels used:

- **Unit** (`vitest`, `npm run test:unit`) — for pure functions changed by a unit (label
  computation, filter predicates, query-key builders, validation/clamping logic).
- **E2E** (`playwright`, `npm run test:e2e`) — for user-observable interaction changes (click
  affordances, confirm flows, expand/collapse defaults).
- **Manual** — for anything Playwright structurally can't catch per `docs/pitfalls/
  react-frontend.md` (Tauri-boundary behavior, drag-and-drop, real-cluster/real-namespace
  verification) and for judgment calls ("does this feel right") that a passing suite doesn't
  confirm.

Each unit's own verification recipe is in `technical-plan.md`'s Worker instructions template
(`tsc -b`, `lint`, `test:unit`, targeted `test:e2e`, manual exercise, Aikido scan). This document
adds the scenarios specific to each batch, plus a short list of scenarios that deserve an explicit
test because they're correctness bugs, not just interaction-model inconsistencies.

## Batch-level scenarios

| Batch | Scenario | Level | Expected |
| --- | --- | --- | --- |
| 0 — Shared infra | Trigger a mutation failure (e.g. stop the sidecar mid-save) on a hook migrated in 0.2 | Manual | A visible error toast appears; the UI does not silently revert with no explanation |
| 0 — Shared infra | Open a feature area with a broken connection, using the pattern from 0.4 | E2E (per-feature spec) | A distinct error state renders, not the empty-state message |
| 0 — Shared infra | Trigger any destructive action migrated to `ConfirmBar` in 0.1 | E2E | `ConfirmBar` renders (not a native `confirm()` dialog, not a bespoke inline banner); confirming performs the action, canceling does not |
| 1 — AKS | Left-click a row on each of the 10 tabs fixed in 1.1 (Deployments/StatefulSets/Jobs/CronJobs/Services/Ingresses/Gateways/GatewayClasses/HTTPRoutes/Secrets) | E2E | Opens the resource's YAML view; row shows pointer cursor + hover highlight only where a real click action exists |
| 1 — AKS | Tab to a context-menu-only row (if any remain after 1.1) and press Enter | E2E | Either a real action fires, or the row is not focusable/does not show clickable affordance |
| 1 — AKS | Force a namespace/pod list fetch to fail (demo-mode error injection or a bad RBAC scope) | E2E + manual | Distinct error banner, not "No resources found" |
| 1 — AKS | **Open a pod shell, then switch cluster context without closing it** | Manual (real cluster, two contexts with a same-named pod if available; otherwise verify via code path — the shell panel unmounts on context change) | Shell panel closes / session is torn down on context change; it must not silently continue against the new context |
| 1 — AKS | Trigger HPA Scale and YAML Apply | E2E | Both go through `ConfirmBar`, not `window.confirm()` |
| 1 — AKS | Sort a resource table by each sortable column; load a namespace with several unhealthy pods | E2E + manual | Sort works; default view surfaces unhealthy-first |
| 2 — Redis | Load a cache with a multi-level namespace tree; click Collapse All, then trigger a key mutation (delete/rename) or Load More | E2E | Tree stays collapsed — the auto-expand effect must not re-fire |
| 2 — Redis | Load a cache with zero keys matching the current pattern, open Keyspace tab | E2E | Shows an explicit empty message, not indefinite "Loading..." |
| 2 — Redis | Click Expand All | E2E | Symmetric to Collapse All, newly present |
| 2 — Redis | Click a Prefix bucket / a slow Ops entry | E2E | Navigates to Keys tab pre-filtered, matching the existing Keyspace→Keys pattern |
| 3 — Service Bus | Force a queue/topic list fetch to fail | E2E + manual | Distinct error state in `EntityTree`/`MessageList`, not "No entities/messages found" |
| 3 — Service Bus | Complete/Resubmit/Purge a message; simulate a server error on one | E2E + manual | Success and failure both produce a visible toast |
| 3 — Service Bus | Trigger the command palette's Refresh action after making an external change to a queue | E2E | Data actually refreshes (query invalidation fixed) |
| 3 — Service Bus | Trigger Batch Replay / bulk Complete / bulk Resubmit / template delete / scheduled-message cancel | E2E | All confirm via `ConfirmBar`, none execute unconfirmed |
| 3 — Service Bus | Collapse/expand topics and the Queues section in a namespace with many entities | E2E | Expand-all/collapse-all present and working; Queues section collapsible |
| 4 — API Client | Open 3+ tabs, close the active (middle) one | E2E | An adjacent tab becomes active, not a blank editor |
| 4 — API Client | Search a collection with a collapsed folder containing a match | E2E | The match is found/shown despite the folder being collapsed |
| 4 — API Client | Click "Collapse all" on a collection with an already-collapsed sub-collection | E2E | Everything ends collapsed; nothing re-expands |
| 4 — API Client | Close a tab with unsaved changes; revert an uncommitted git change; delete an environment | E2E | Each dialog's confirm button reads correctly ("Close"/"Discard changes"/"Delete") and environment delete now requires confirmation |
| 4 — API Client | Stage a file, then edit it further, then click Diff | Manual (requires a real git repo fixture) | Diff behavior matches whichever resolution was chosen (index-diff or explicit staged/working toggle) — verify it no longer silently shows unstaged edits as if they were staged |
| 5 — Monitoring | Click an alert rule row (not the pencil icon); delete a rule; disable a rule | E2E | Row click opens edit; delete requires confirmation; disable shows an undo toast |
| 5 — Monitoring | Force the rules/history fetch to fail | E2E + manual | Distinct error state, not empty |
| 5 — Monitoring | Attempt to save a rule with only a name filled in | E2E | Save is disabled / validation error shown |
| 6 — Storage | Upload and then download a binary blob (image or zip) via the fixed Download button | Manual (binary round-trip: compare downloaded file bytes/hash to the source) | Downloaded file is byte-identical to the source, or the button clearly redirects to a signed-URL flow instead of producing a corrupt file |
| 6 — Storage | Navigate two and three levels deep into a container with virtual folders | E2E | Every breadcrumb crumb shows the correct, non-blank segment label |
| 6 — Storage | Upload a blob with a name that already exists; recover a soft-deleted blob | E2E | Both require confirmation |
| 6 — Storage | Filter the blob list for a name known to exist beyond the currently-loaded page | Manual (large container) | Either the filter finds it (server-side) or the UI explicitly states the search scope and offers to load more |
| 7 — Agent | Force a confirm/reject call to fail on a pending action | E2E + manual | Visible error state on the card, not a silent return to default |
| 7 — Agent | Trigger a long multi-tool turn | Manual | Tool-progress text updates during the turn; a Stop button is present and actually cancels |
| 7 — Agent | Trigger a tool failure mid-turn that the agent recovers from | E2E + manual | The reasoning trace's collapsed toggle indicates a failure occurred, without requiring it to be expanded first |
| 8 — Settings | Configure a Service Bus namespace with Connection String auth | E2E | The connection-string field exists and can be saved |
| 8 — Settings | Use "Test connection" on each of AKS/Service Bus/Redis/Storage | Manual (needs live or demo backends) | Each reports pass/fail with a diagnostic |
| 8 — Settings | Save a settings field with a value the backend will reject (simulate if possible) | E2E + manual | A visible error toast appears; the field does not just silently revert |
| 8 — Settings | Type quickly into an Agent settings field (base URL, model name) | Manual | Feels the same (commit-on-blur) as every other migrated settings field, no per-keystroke lag |
| 9 — Dashboard | Click each "Live Watch" tile | E2E | Lands on the tab that actually shows the referenced number |
| 9 — Dashboard | Disconnect a configured Service Bus namespace, compare the dashboard health tile to the footer status bar | Manual | Both agree (no contradictory "Ready" vs "Unavailable" shown simultaneously) |

## Regression check

Before merging each batch, run the full existing suite to confirm no unrelated regression:

- `npx tsc -b` (whole `web/`)
- `npm run lint`
- `npm run test:unit` (full run, not just the touched area)
- `npm run test:e2e` (full run) — note the existing Playwright traps in `docs/pitfalls/
  react-frontend.md` (throwaway shared appdata across a file's tests; `addInitScript` reruns on
  every navigation; waiting on an already-correct value doesn't wait) apply to any new spec written
  for this plan.

## Manual verification checklist (owner: Sebastien, post-merge)

These specifically can't be fully confirmed by an automated suite:

1. **AKS cross-cluster shell safety (unit 1.4).** Against two real contexts with a same-named pod
   if available — confirm the shell truly disconnects/reconnects correctly on context switch, not
   just that the panel unmounts in code.
2. **Storage binary download integrity (unit 6.1).** Round-trip a real image/zip/binary blob
   through Upload → Download and diff bytes against the original.
3. **Settings "Test connection" (unit 8.2).** Against real AKS/Service Bus/Redis/Storage
   credentials — confirm both the pass and fail paths report something true and useful.
4. **Agent tool-call progress and cancel (unit 7.3).** A real multi-tool investigation turn against
   live data, long enough to exercise both the progress text and the Stop button.
5. **Aikido scan sign-off** per `docs/security/aikido-mcp-scan.md` for every unit — tracked
   per-unit in `status.md`, not re-litigated here.
