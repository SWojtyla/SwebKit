# Test Plan — Monitoring Rebuild

Automated coverage is added wave-by-wave alongside the code (see `technical-plan.md`'s per-wave
**Test:** notes) — this file is the consolidated scenario list and traceability, not a separate
implementation task.

## Automated — Sidecar (`SwebKit.Core.Tests` / a new sidecar test project if one doesn't already exist)

| # | Scenario | Wave |
| - | -------- | ---- |
| A1 | `AlertRuleRepository` loads a valid `monitoring-alerts.json` | A |
| A2 | Corrupt primary file falls back to `.bak` | A |
| A3 | Corrupt primary + missing `.bak` resets to empty **and** preserves the unreadable file via `PreserveUnreadableFile` (does not silently destroy it on next save) | A |
| A4 | Round-trip serialization of `MonitoringAlertRule` (all 4 param types, both severities, all 8 in-scope sources) matches sidecar `JsonOptions` (camelCase, string enums) | A |
| B1 | Each of the 8 signal sources returns `Ok` when below threshold | B |
| B2 | Each of the 8 signal sources returns `Firing` when at/above threshold, with a non-empty `Message` | B |
| B3 | Each source returns `Error` (not an unhandled throw) when its underlying client call fails | B |
| C1 | Due-scheduling: a rule is not re-evaluated before `IntervalSeconds` has elapsed | C |
| C2 | Cooldown: a `Firing` rule does not fire again within `CooldownMinutes` | C |
| C3 | Backoff: consecutive `Error`/`Skipped` results increase the next-evaluate delay, capped at 600s | C |
| C4 | Backoff resets to normal interval after an `Ok`/`Firing` result | C |
| C5 | Concurrency: no more than 4 signal-source evaluations run simultaneously | C |
| C6 | Ring buffer caps at 200 fired events, evicting oldest first | C |
| C7 | A rule mutation (create/update/delete) is picked up by the next tick without a sidecar restart | C |
| D1 | `GET/POST/PUT/DELETE /api/monitoring/rules` CRUD round-trip | D |
| D2 | `GET /api/monitoring/history` returns the current ring buffer, most-recent-consistent with what fired | D |
| D3 | `GET /api/monitoring/stream` pushes a `data:` line per fired event in `text/event-stream` format | D |
| D4 | All Monitoring routes respect `DemoModeService.IsDemoMode` consistently with sibling endpoints | D |
| D5 | All Monitoring routes are reachable only from allowed origins (reuses the shared CORS test if one exists for other endpoint groups) | D |

## Automated — React (component tests)

| # | Scenario | Wave |
| - | -------- | ---- |
| E1 | `AlertRuleRow` renders the correct status dot for Ok/Firing/Skipped/Error | E |
| E2 | `AlertRuleGroups` groups rules by source and collapses/expands | E |
| E3 | `AlertRuleDialog` shows the correct field set per selected source (AKS/Service Bus/Redis) | E |
| E4 | `AlertHistoryPanel` snooze hides an event without a network call (client-side only, matching MAUI) | E |
| E5 | `useMonitoringStream` reconnects/cleans up its `EventSource` on unmount | E |

## Automated — E2E (Playwright, demo mode)

| # | Scenario | Wave |
| - | -------- | ---- |
| F1 | Create a rule → appears in the grouped list, persists across reload | E/F |
| F2 | Edit a rule → change reflected in the list and in subsequent evaluations | E/F |
| F3 | Delete a rule → removed from the list and no longer evaluated | E/F |
| F4 | If demo mode has a scripted threshold-crossing tick (mirror `PodsTab.tsx`'s pattern from `demo-mode-parity`): a fired event appears in history live via SSE, and a native/browser notification fires | E/F |
| F5 | Full existing Playwright suite (140+ tests) still passes — the sidecar gained a background service and new routes, confirm no port/CORS/startup regression | F |

## Manual (needs the user or a real environment)

- Real AKS namespace: confirm `AksPodHealth`/`AksPodRestartRate`/`AksNamespaceHealthScore` fire
  correctly against real pod state, not just demo data.
- Real Service Bus namespace: confirm DLQ depth / active depth / dead subscription sources against
  real entities.
- Real Redis cache: confirm memory usage / connected clients thresholds against a real instance.
- Built installer / packaged-app smoke test (per `tauri-security-hardening`'s precedent) — confirm
  the background evaluation service starts correctly in a bundled build, not just `tauri dev`.
- Aikido security scan per `docs/security/aikido-mcp-scan.md` (no Aikido tooling available in an
  agent session — needs the user, same gap noted on every other active feature this session).

## Traceability

Every finding this rebuild closes traces back to `../post-migration-ux-review/index.md` finding #1
and `../post-migration-ux-review/technical-plan.md` §1.2. Do not mark that item's scope decision
undone if this feature's own checklist has gaps — the decision is recorded there; implementation
tracking lives entirely in this folder's `status.md`.
