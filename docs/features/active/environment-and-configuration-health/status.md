# Status - environment-and-configuration-health

---

title: "Status - environment-and-configuration-health"
owner: "GitHub Copilot"
state: "Review"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-13"

---

## Quick summary

Wave 3 implementation is now code-complete for the planned scope. The final slice added explicit read-only live checks, richer per-area drill-through, and stable component-level readiness coverage on top of the earlier report, dashboard, and settings work.

Jira: not linked

Current focus: complete manual UI validation and close-out so the feature can move from review to done/archive without overstating validation.

## Progress checklist

### Planning

- [x] Define scope and delivery waves
- [x] Capture sequencing and safety constraints
- [x] Finalize health state taxonomy and report contract
- [x] Finalize readiness summary and configuration-gap normalization rules

### Implementation focus

- [x] First-run checklist and setup CTA flow
- [x] Credential/config health and connection-health overview
- [x] Readiness summary and configuration-gap drill-through
- [ ] Automated and manual validation

## Completed

- Created the active feature folder and core planning docs.
- Identified the main existing seams to extend: `DashboardPage`, `SettingsPage`, `HealthTile`, `ProfileRepository`, `ConnectionStateService`, and `WindowsCredentialStore`.
- Scoped the feature around read-only readiness, not new resource operations.
- Recorded the dependency on `remove-environment-model` and removed profile-environment comparison from the planned outcome.
- Added `ConfigurationHealthService` plus safe readiness models for capability-area status, credential-reference presence, and action-first setup items.
- Added a dashboard readiness surface with setup checklist links into the owning Settings sections.
- Added current-section readiness context to Settings so operators can see safe credential-reference status and missing prerequisites before saving or testing a section.
- Added focused unit coverage for the new readiness report builder.
- Added an explicit `IConfigurationProbeService` that reuses the existing Service Bus, AKS, Redis, Storage, DevOps, and Observability seams for read-only, time-budgeted live checks.
- Extended the shared readiness report contract with session-scoped live probe results so `Configured` can upgrade to `Ready` or fall back to `Warning` without page-local heuristics.
- Extracted the dashboard and settings readiness surfaces into standalone shared components and added focused bUnit coverage for the new checklist, handoff, and drill-through behavior.

## Remaining

- Run manual UI validation for first-run, partially configured, and failed-live-check flows before marking the feature done.
- Add or validate the planned E2E readiness coverage once the existing Playwright/Windows App SDK harness can launch the app reliably in this environment.
- Decide whether to archive immediately after manual validation or leave the feature in `Done` until a user confirms close-out.

## Blockers

- None, but the feature should not revive profile-environment comparison assumptions removed by `remove-environment-model`.

## Validation

- Test Plan: `test-plan.md`
- Validation status: `dotnet build SwebKit.slnx` succeeded. Focused readiness tests passed in `SwebKit.Core.Tests` (6/6) and `SwebKit.App.Tests` (7/7). Manual UI validation has not been run yet.

## Notes

- This feature should improve trust before the operator enters a failing page, not just reformat page-level errors after the fact.
- The old environment-comparison wave is intentionally removed; if comparison is ever reintroduced, it must be based on a new explicit model rather than the deleted local environment system.
- Route-page bUnit coverage remains unsuitable because the app test project does not materialize `DashboardPage` and `SettingsPage` reliably. The current automated UI coverage now targets extracted readiness components instead of the route pages.
