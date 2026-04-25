# Test Plan - winui3-cutover-audit-hardening

---

title: "Test Plan - winui3-cutover-audit-hardening"
owner: ""
status: "In Progress"
created: "2026-04-24"
updated: "2026-04-25"

---

## Goal

Validate that the native WinUI host is not only present, but credible enough to replace the MAUI host without hiding parity debt, runtime blocker paths, or missing validation seams.

## Scope

- In scope: route-by-route parity closure, shared WinUI hardening, runtime blocker triage, and cutover readiness evidence
- In scope: shell/dashboard/settings, Service Bus, AKS, Redis, Storage, Pipelines/Releases, and Observability
- Out of scope: Incident Timeline migration, cosmetic-only polish, and deleting `SwebKit.App` before readiness gates pass

## Main scenarios (priority)

1. Scenario: the current WinUI baseline is a real native host, not a partial shell — Expected result: the app launches, routes into each migrated page, and does not rely on MAUI fallback UI.
2. Scenario: parity gaps are closed in a controlled order — Expected result: every domain meets its agreed cutover checklist, and remaining debt is explicit rather than implicit.
3. Scenario: auth-dependent failures are survivable — Expected result: Pipelines and Observability show actionable failure/readiness states instead of debugger-break investigations or misleading empty states.
4. Scenario: shared page primitives replace repeated page-local patterns — Expected result: state views, metric cards, and detail-pane layouts become reusable and reduce XAML drift.
5. Scenario: debugger-break investigation is evidence-led — Expected result: any remaining unhandled exception is tied to a reproducible route/action and real logs, not the generated `App.g.i.cs` file alone.
6. Scenario: cutover readiness is measurable — Expected result: the repo can answer whether `SwebKit.App` is removable using manual and automated validation, not intuition.

## Automated coverage

- Build validation: `build-winui` must stay green after every parity or hardening slice.
- Existing domain tests: `tests/SwebKit.Core.Tests/`, `tests/SwebKit.Azure.Tests/`, `tests/SwebKit.Kubernetes.Tests/`, and `tests/SwebKit.DevOps.Tests/` must remain green when touched behavior changes.
- WinUI coverage target: keep `tests/SwebKit.WinUI.Tests/` green and expand focused tests into shell/navigation/theme state and deeper page view-model seams that currently carry high orchestration load.
- End-to-end target: add one native-host smoke journey per major area once the manual checkpoint is stable.

## Test data and setup

- Demo mode remains the baseline smoke path for shell and page activation validation.
- Live validation needs representative Service Bus, AKS, Redis, Storage, DevOps, and Observability config in `%APPDATA%/SwebKit` plus matching credentials in Windows Credential Manager and Azure login state.
- Pipelines validation needs a working Azure DevOps PAT path; Observability validation needs a working Azure credential chain.

## Manual checks

- Check: native host launch baseline — steps: launch the WinUI app, confirm it stays alive, and verify the default route opens without placeholder fallback.
- Check: shell checkpoint — steps: visit Dashboard, Settings, Service Bus, AKS, Redis, Storage, Pipelines, and Observability; confirm the route map is native and the shell chrome remains coherent.
- Check: auth-dependent readiness — steps: validate Pipelines and Observability under both demo mode and a live-configured machine; confirm failures surface as actionable status rather than silent empties.
- Check: debugger-break triage — steps: reproduce the reported failing interaction under a debugger, capture the route, action, and real exception, then verify whether the failure is handled, unhandled, or environment-driven.
- Check: cutover gate — steps: rerun the agreed manual smoke set after each hardening wave and confirm the remaining debt list shrinks rather than shifts.

## Regression risks & mitigations

- Risk: the repo keeps treating broad phase labels as proof of readiness — Mitigation: require domain-level gap closure and manual sign-off.
- Risk: new page work deepens XAML duplication before shared primitives are finished — Mitigation: block further widening until shared refactors land.
- Risk: environment-specific auth failures are mistaken for host instability — Mitigation: separate infrastructure-readiness failures from real WinUI host failures in validation notes.

## Acceptance criteria

- Every cutover-critical domain has an explicit parity status: complete, deferred with justification, or blocked.
- The debugger-break path is either reproduced with a real exception and owner, or downgraded from blocker status with evidence.
- At least one focused automated validation seam exists for the WinUI host beyond `build-winui`.
- Manual smoke validation has been executed and recorded.
- The repo has a defensible cutover recommendation.

## Validation status

- Automated: `build-winui` currently passes; `dotnet test tests/SwebKit.WinUI.Tests/SwebKit.WinUI.Tests.csproj` also passes with 8 tests covering readiness formatter classification plus Pipelines/Observability readiness-state and generic-error gating. Focused lifecycle tests for the shared page scheduler are still missing.
- Manual: Not started

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
