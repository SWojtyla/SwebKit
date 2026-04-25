# Feature Overview - winui3-cutover-audit-hardening

---

title: "Feature Overview - winui3-cutover-audit-hardening"
owner: ""
status: "In Progress"
jira: "not linked"
created: "2026-04-24"
updated: "2026-04-25"

---

## Goal

Coordinate the final WinUI migration cutover after the remaining work has been split into feature-specific plans, and hold the go/no-go recommendation on retiring `SwebKit.App`.

## Value

This feature is no longer the monolithic owner of every remaining parity task. The repo now has separate active plans for layout redesign, settings completeness, and each remaining domain slice. Keeping one active umbrella still matters because someone has to own dependency order, cross-feature smoke validation, and the final cutover recommendation.

## Scope

### Wave 0 - Planning split and dependency control

- Freeze the old monolithic plan and move the remaining work into feature-specific active folders.
- Keep the dependency order explicit: layout redesign first, settings completeness next, then the domain parity slices.

### Wave 1 - Cross-feature integration tracking

- Track which parity gaps are still cutover-critical versus safe to defer.
- Keep the active feature matrix current as layout, settings, and domain slices land.
- Preserve the already-landed hardening work as part of the baseline cutover evidence.

### Wave 2 - Cutover validation

- Run and record the manual and automated cutover gate once the dependency features are implemented.
- Update architecture or functionality docs when the WinUI host becomes the primary supported path.
- Produce the explicit cutover recommendation: ready, not ready, or ready with bounded follow-up debt.

## Out of scope

- Owning the implementation backlog for any one domain-specific parity feature.
- Reintroducing a single global parity checklist after the split.
- Deleting `SwebKit.App` before the cutover gate is actually satisfied.

## Dependencies

- Baseline archive: `docs/features/archive/winui3-migration/`
- Active dependency features:
  - `docs/features/active/winui3-layout-redesign/`
  - `docs/features/active/winui3-settings-completeness/`
  - `docs/features/active/winui3-service-bus-parity/`
  - `docs/features/active/winui3-aks-parity/`
  - `docs/features/active/winui3-redis-parity/`
  - `docs/features/active/winui3-storage-parity/`
  - `docs/features/active/winui3-pipelines-releases-parity/`
  - `docs/features/active/winui3-observability-parity/`
- Architecture constraints: `docs/architecture/architecture.md`, `docs/architecture/design.md`, `docs/architecture/codebase-guide.md`
- Focused validation command: VS Code task `build-winui`

## Risks & mitigations

- Risk: the repo drifts back into one implicit global plan.  
  Mitigation: keep each remaining migration slice in its own active feature folder and remove duplicated scope from this umbrella.
- Risk: split features land without a real cutover gate.  
  Mitigation: keep this feature focused on cross-feature smoke validation and the final recommendation.
- Risk: environment-sensitive routes still blur host failures with credential failures.  
  Mitigation: keep the readiness-hardening evidence in this umbrella until the cutover gate closes.

## Related documents

- Baseline migration: `docs/features/archive/winui3-migration/`
- Feature coordination module: `frontend.md`
- Current cutover decisions: `decisions.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `frontend.md`, `decisions.md`
