# Feature Overview - winui3-cutover-audit-hardening

---

title: "Feature Overview - winui3-cutover-audit-hardening"
owner: ""
status: "Planned"
jira: "not linked"
created: "2026-04-24"
updated: "2026-04-24"

---

## Goal

Audit the current WinUI 3 migration baseline, close the remaining parity and hardening gaps in a controlled order, and establish objective cutover criteria before `SwebKit.App` can be removed.

## Value

The original `winui3-migration` feature now contains three different jobs at once: first-pass route migration, parity closure, and structural cleanup. That makes progress look larger than the validated surface actually is. Splitting the follow-up work gives the repo a credible cutover path: one feature owns the already-landed baseline, and this feature owns the deeper audit, refactoring, validation, and go/no-go cutover decisions.

## Scope

### Wave 0 — Baseline checkpoint and blocker triage

- Confirm the current native WinUI baseline and freeze further scope expansion until parity and hardening work is routed through this feature.
- Turn the remaining work into a concrete gap matrix by domain: shell/dashboard/settings, Service Bus, AKS, Redis, Storage, Pipelines/Releases, and Observability.
- Triage the current debugger-break investigation without editing generated XAML files. The generated `App.g.i.cs` break site is a symptom surface, not the source of truth.

### Wave 1 — Parity closure by domain

- Close the remaining operator-facing parity gaps that were still inside the original migration scope.
- Reorder the work so that shared cutover dependencies land before additional feature expansion.
- Require every parity slice to leave the app bootable and `build-winui` green.

### Wave 2 — Shared refactors and hardening

- Finish the missing shared WinUI primitives instead of continuing with page-local XAML duplication.
- Extract oversized page/view-model responsibilities where the migration has started to accumulate too much per-page orchestration logic.
- Normalize connection/readiness failure handling across pages that depend on Azure DevOps or Azure credentials.
- Add the missing validation seams needed for repeatable WinUI verification.

### Wave 3 — Cutover readiness

- Add manual smoke coverage and focused automated coverage for the native host.
- Update architecture and codebase docs when the WinUI host becomes the primary entry path.
- Produce an explicit cutover recommendation: ready, not ready, or ready with bounded follow-up debt.

## Out of scope

- Incident Timeline migration or redesign.
- New operator features unrelated to WinUI parity or cutover safety.
- Pure visual polish that does not reduce parity or hardening risk.
- Deleting `SwebKit.App` before the readiness gates in this feature are satisfied.

## Dependencies

- Related active feature: `docs/features/active/winui3-migration/`
- Architecture constraints: `docs/architecture/architecture.md`, `docs/architecture/design.md`, `docs/architecture/codebase-guide.md`
- Functional baselines: `docs/architecture/functionalities/aks.md`, `docs/architecture/functionalities/redis.md`, `docs/architecture/functionalities/storage.md`, `docs/architecture/functionalities/releases.md`, `docs/architecture/functionalities/observability.md`
- Pitfall files that apply: `docs/pitfalls/dotnet-csharp.md`, `docs/pitfalls/azure-sdk.md`
- Focused validation command: VS Code task `build-winui`

## Risks & mitigations

- Risk: The current migration baseline keeps expanding before it is manually validated.  
  Mitigation: treat this feature as the only place where remaining parity and hardening work is planned and tracked.
- Risk: Route-level parity gaps are hidden by broad phase headlines.  
  Mitigation: maintain a domain gap matrix in `frontend.md` and use it instead of phase labels alone.
- Risk: The debugger break in generated XAML code is misdiagnosed as an `App.xaml` bug.  
  Mitigation: treat generated files as symptoms only, capture the failing route/action, and inspect real runtime evidence before changing exception policy.
- Risk: Shared primitives remain incomplete and page-local XAML keeps multiplying.  
  Mitigation: prioritize refactors for shared state views, metric/detail surfaces, and repeated page activation patterns before deeper page work.
- Risk: Auth-dependent pages stay hard to validate because credential failures surface differently per page.  
  Mitigation: add a common readiness/auth failure strategy for Pipelines and Observability.

## Related documents

- Baseline migration: `docs/features/active/winui3-migration/`
- Pipelines and releases functionality: `docs/architecture/functionalities/releases.md`
- Observability functionality: `docs/architecture/functionalities/observability.md`

## Quick links

- Jira: not linked
- Status: `status.md`
- Tests: `test-plan.md`
- Implementation modules: `frontend.md`, `decisions.md`