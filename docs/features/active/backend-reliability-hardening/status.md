# Status - backend-reliability-hardening

---

title: "Status - backend-reliability-hardening"
owner: "GitHub Copilot"
state: "Planned"
jira: "not linked"
branch: ""
started: "2026-04-11"
last_updated: "2026-04-11"

---

## Quick summary

Planning is complete for a correctness-first backend hardening feature that addresses six verified issues without broad architectural churn.

Jira: not linked

Current focus: start with Wave 1 design and implementation for DevOps configuration isolation, profile load diagnostics, and `AppEventBus` dispatch semantics because those define the failure model that the remaining fixes should follow.

## Progress checklist

### Planning

- [x] Confirmed the verified issue set and kept the scope limited to those hotspots
- [x] Mapped affected code, doc, and test areas across Core, DevOps, Azure, Redis, Observability, and App
- [x] Chosen a wave order that preserves project boundaries and additive contract patterns
- [x] Defined acceptance criteria around complete operations, explicit failures, and regression safety

### Implementation focus

- [ ] Replace shared mutable DevOps configuration with per-configuration real client creation
- [ ] Make DLQ complete and resubmit behavior exhaustive across receive batches
- [ ] Replace fabricated Redis set-member continuation logic with source-backed cursor behavior
- [ ] Surface profile load failures instead of silently resetting state
- [ ] Stop `AppEventBus` sync publish from logging false async-handler cast failures
- [ ] Bound App Insights row projection before truncation and add direct regression coverage
- [ ] Align functionality docs and validation notes with the shipped behavior

## Completed

- Captured the affected backend hotspots and preserved strengths from the current architecture.
- Scoped the feature to correctness and error handling rather than a general backend refactor.
- Identified the minimal app adoption work required to keep DI ownership in `SwebKit.App`.

## Remaining

- Implement the three planned waves.
- Decide the narrowest direct test seam for `AzureAppInsightsProvider` truncation coverage.
- Update the affected functionality docs in the same change set as implementation.

## Blockers

- No implementation blocker identified.
- Jira is not linked. Informational only.

## Validation

- Test plan: `test-plan.md`
- Validation status: Planning updated; code validation not started

## Notes

- Preserve the existing DevOps `AddStandardResilienceHandler` registration.
- Prefer additive contracts or factories over breaking interface changes.
- Do not widen this feature into a generic repository-hardening sweep unless a very small shared helper emerges naturally during implementation.