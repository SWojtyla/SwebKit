# Status - environment-and-configuration-health

---

title: "Status - environment-and-configuration-health"
owner: "GitHub Copilot"
state: "In Progress"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-13"

---

## Quick summary

Wave 3 is now in implementation. The first slice landed a canonical configuration-health report, dashboard checklist and readiness overview, and settings section readiness context without reintroducing the removed environment-comparison model.

Jira: not linked

Current focus: extend the local config-and-credential readiness model into read-only live probes and deeper per-area drill-through without collapsing the configured vs ready distinction.

## Progress checklist

### Planning

- [x] Define scope and delivery waves
- [x] Capture sequencing and safety constraints
- [x] Finalize health state taxonomy and report contract
- [x] Finalize readiness summary and configuration-gap normalization rules

### Implementation focus

- [x] First-run checklist and setup CTA flow
- [ ] Credential/config health and connection-health overview
- [ ] Readiness summary and configuration-gap drill-through
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

## Remaining

- Decide which checks run automatically vs only on explicit operator refresh.
- Add read-only live probes for areas where local configuration alone should not produce `Ready`.
- Expand readiness drill-through so capability areas can explain richer configuration gaps without dumping raw config.
- Add stable UI-level automated coverage for the new dashboard and Settings surfaces.

## Blockers

- None, but the feature should not revive profile-environment comparison assumptions removed by `remove-environment-model`.

## Validation

- Test Plan: `test-plan.md`
- Validation status: `dotnet build SwebKit.slnx` succeeded. `ConfigurationHealthServiceTests` passed (4/4). Manual UI validation has not been run yet.

## Notes

- This feature should improve trust before the operator enters a failing page, not just reformat page-level errors after the fact.
- The old environment-comparison wave is intentionally removed; if comparison is ever reintroduced, it must be based on a new explicit model rather than the deleted local environment system.
- A first attempt at route-page bUnit coverage was removed because the app test project does not materialize `DashboardPage` and `SettingsPage` the same way it materializes other page components. The current automated coverage stays at the report-builder layer until a stable UI harness is chosen.
