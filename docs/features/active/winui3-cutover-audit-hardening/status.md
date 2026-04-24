# Status - winui3-cutover-audit-hardening

---

title: "Status - winui3-cutover-audit-hardening"
owner: ""
state: "Planned"
jira: "not linked"
branch: "winui-rewrite"
started: "2026-04-24"
last_updated: "2026-04-24"

---

## Quick summary

Follow-up feature created to own parity audit, hardening, refactoring, and cutover readiness after the initial WinUI migration baseline. The next meaningful deliverable is a validated gap matrix plus a reproducible blocker list.

**Jira:** not linked

**Current focus:** checkpoint the landed WinUI baseline, capture the remaining parity debt by domain, and turn the current runtime failures into explicit hardening tasks.

## Progress checklist

### Wave 0 — Checkpoint and audit

- [x] Related baseline feature identified (`winui3-migration`)
- [x] Current route coverage and shared-primitives surface reviewed
- [x] Runtime blocker evidence gathered from launch behavior and Windows Application event logs
- [ ] Domain gap matrix reviewed against current WinUI implementation
- [ ] Baseline checkpoint accepted as the new starting point for follow-up work

### Wave 1 — Domain parity closure

- [ ] Shell/dashboard/settings parity gaps closed
- [ ] Service Bus parity gaps closed
- [ ] AKS parity gaps closed
- [ ] Redis parity gaps closed
- [ ] Storage parity gaps closed
- [ ] Pipelines/Releases parity gaps closed
- [ ] Observability parity gaps closed

### Wave 2 — Refactors and hardening

- [ ] Shared state, metric, and detail-pane primitives added
- [ ] Repeated WinUI page activation pattern consolidated
- [ ] Oversized page/view-model seams reduced where needed
- [ ] Auth/readiness failure handling normalized across Azure-backed pages
- [ ] Focused WinUI automated coverage introduced

### Wave 3 — Cutover readiness

- [ ] Manual smoke suite executed against the native host
- [ ] WinUI test/docs updates aligned with the cutover path
- [ ] Architecture docs updated for the new primary host
- [ ] Explicit cutover recommendation recorded

## Completed

- Verified that the WinUI app launches and remains alive; the current blocker is not a launch-time crash.
- Confirmed that the current migration already has native routed pages for dashboard, settings, Service Bus, AKS, Redis, Storage, Pipelines, and Observability.
- Confirmed that shared shell primitives exist, but shared page primitives remain partial: the repo currently has `PageScaffold` plus shell panels, not the broader `StateView` / metric / detail host set.
- Captured concrete runtime evidence for two current hardening issues:
  - Pipelines baseline load fails when Azure DevOps connection validation does not succeed.
  - Observability resource discovery fails when `DefaultAzureCredential` cannot acquire a token.

## Remaining

- Convert the current migration's broad parity promises into a tracked per-domain checklist.
- Decide which open items remain true cutover requirements versus deliberate post-cutover backlog.
- Reproduce the debugger-break path with exact route/action steps if it still occurs under a debugger.
- Add the missing validation surface for the WinUI host: focused tests, first manual smoke pass, and cutover readiness gates.

## Blockers

- No exact repro steps yet for the debugger-break path beyond the generated `App.g.i.cs` symptom line.
- Live validation of Pipelines and Observability remains environment-sensitive because the current machine state does not provide a successful Azure DevOps or Azure credential path.

## Validation

- Test Plan: link to `test-plan.md`
- Validation status: In progress

## Notes

- The active `winui3-migration` feature should stop widening scope and treat this feature as the source of truth for remaining parity, hardening, and cutover work.