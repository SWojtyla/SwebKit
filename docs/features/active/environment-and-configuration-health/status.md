# Status - environment-and-configuration-health

---

title: "Status - environment-and-configuration-health"
owner: "GitHub Copilot"
state: "Planned"
jira: "not linked"
branch: ""
started: "2026-04-12"
last_updated: "2026-04-12"

---

## Quick summary

The readiness planning docs remain useful, but the feature has been re-scoped by `remove-environment-model`: it now targets single-configuration readiness only. The next step is to define the health-report contract and read-only probe rules without reintroducing profile-environment comparison.

Jira: not linked

Current focus: lock down the health model and credential-reporting rules for the single-config readiness experience before page work starts.

## Progress checklist

### Planning

- [x] Define scope and delivery waves
- [x] Capture sequencing and safety constraints
- [ ] Finalize health state taxonomy and report contract
- [ ] Finalize readiness summary and configuration-gap normalization rules

### Implementation focus

- [ ] First-run checklist and setup CTA flow
- [ ] Credential/config health and connection-health overview
- [ ] Readiness summary and configuration-gap drill-through
- [ ] Automated and manual validation

## Completed

- Created the active feature folder and core planning docs.
- Identified the main existing seams to extend: `DashboardPage`, `SettingsPage`, `HealthTile`, `ProfileRepository`, `ConnectionStateService`, and `WindowsCredentialStore`.
- Scoped the feature around read-only readiness, not new resource operations.
- Recorded the dependency on `remove-environment-model` and removed profile-environment comparison from the planned outcome.

## Remaining

- Define the canonical health-report and configuration-gap models.
- Decide which checks run automatically vs only on explicit operator refresh.
- Implement the first-run checklist, readiness overview, and configuration-gap UI.

## Blockers

- None, but the feature should not revive profile-environment comparison assumptions removed by `remove-environment-model`.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started.

## Notes

- This feature should improve trust before the operator enters a failing page, not just reformat page-level errors after the fact.
- The old environment-comparison wave is intentionally removed; if comparison is ever reintroduced, it must be based on a new explicit model rather than the deleted local environment system.
