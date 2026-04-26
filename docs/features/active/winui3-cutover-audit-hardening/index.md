# Feature Overview - winui3-cutover-audit-hardening

---

title: "Feature Overview - winui3-cutover-audit-hardening"
owner: ""
status: "Done"
jira: "not linked"
created: "2026-04-24"
updated: "2026-04-26"

---

## Goal

Coordinate the WinUI migration split and preserve the cutover decision surface as a historical checkpoint, while recording that no final retirement recommendation for `SwebKit.App` was made before this umbrella was closed.

## Value

This feature is no longer the monolithic owner of every remaining parity task. It served as the coordination checkpoint that split the work into smaller feature-specific slices, preserved shared execution contracts, and kept the cutover-critical gaps visible. It now closes as a historical coordination record rather than an active execution surface, and future work should reopen dedicated one-by-one follow-up slices instead of reviving the umbrella implicitly.

## Scope

### Wave 0 - Planning split and contract control

- Freeze the old monolithic plan and move the remaining work into feature-specific active folders.
- Keep ownership boundaries explicit so multiple agents can work in parallel without reopening the same global scope.
- Treat shared layout primitives, the native settings repair path, and cutover-critical labels as coordinated contracts rather than serialized blockers.

### Wave 1 - Cross-feature integration tracking

- Track which parity gaps are still cutover-critical versus safe to defer.
- Keep the active feature matrix current as layout, settings, and domain slices land.
- Preserve the already-landed hardening work as part of the baseline cutover evidence.

### Wave 2 - Cutover validation

- Run and record the manual and automated cutover gate once the coordinated feature set is ready for integration review.
- Update architecture or functionality docs when the WinUI host becomes the primary supported path.
- Produce the explicit cutover recommendation: ready, not ready, or ready with bounded follow-up debt.

## Out of scope

- Owning the implementation backlog for any one domain-specific parity feature.
- Reintroducing a single global parity checklist after the split.
- Deleting `SwebKit.App` before the cutover gate is actually satisfied.

## Dependencies

- Baseline archive: `docs/features/archive/winui3-migration/`
- Active coordinated features:
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

## Parallel execution contract

- This umbrella does not serialize feature execution unless two slices need the same shared surface.
- Layout redesign and settings completeness are treated as current shared baselines for downstream plans; their remaining review work is tracked in their own folders.
- Domain features own page-local adoption of shared layout primitives and readiness-to-settings flows inside their own routes.
- Any proposal that changes shared file ownership, the current settings navigation contract, or cutover-critical labels must be recorded here before it changes more than one feature plan.

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
