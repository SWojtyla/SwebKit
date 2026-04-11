# Status - frontend-composition-hardening

---

title: "Status - frontend-composition-hardening"
owner: "GitHub Copilot"
state: "Planned"
jira: "not linked"
branch: ""
started: "2026-04-11"
last_updated: "2026-04-11"

---

## Quick summary

Planning is complete for a narrow frontend hardening pass that keeps current workflows intact while removing the most fragile composition points in `MainLayout`, Observability, Service Bus, and AKS bootstrap paths.

Jira: not linked

Current focus: begin with shared factory and connector seams plus shell error presentation so the page refactors land on stable abstractions instead of duplicating more page-owned infrastructure logic.

## Progress checklist

### Planning

- [x] Confirmed the reviewed problem areas and the strengths that must be preserved
- [x] Kept scope distinct from `incident-timeline-workbench`
- [x] Defined wave plan, risks, and validation targets

### Implementation focus

- [ ] Wave 1 - add shared provider and client creation seams plus shell error presentation
- [ ] Wave 2 - harden Observability provider activation and failure-to-logs coordination
- [ ] Wave 3 - move Service Bus and AKS bootstrap logic behind injected seams
- [ ] Wave 4 - add shell and composition tests and align functionality docs

## Completed

- Verified direct concrete construction in `ObservabilityPage`, `ServiceBusPage`, and `AksPage`.
- Verified console-only shell failure handling in `MainLayout` background initialization and keyboard shortcut registration.
- Verified timing-based failure-to-logs coordination in `ObservabilityPage`.
- Recorded that `SwebKitComponentBase`, `PageDataCache`, existing component coverage, and cancellation awareness are strengths to preserve.

## Remaining

- Finalize the exact contract names and folder placement for the new seams during implementation.
- Implement the scoped refactors without changing routes, demo-mode behavior, or page-level UX.
- Add regression coverage around shell composition, DI registration, and last-request-wins behavior.
- Update the touched functionality docs once the actual service and contract names are known.

## Blockers

- Jira is not linked. Informational only.
- Broader frontend cleanup outside the scoped pages is intentionally deferred and should not expand this feature.

## Validation

- Test plan: `test-plan.md`
- Validation status: Planning updated; code validation not started

## Notes

- Keep existing routes and area identifiers stable.
- Do not overlap `incident-timeline-workbench` or add new user workflows in this feature.
- Preserve `PageDataCache` snapshot behavior and current cancellation-first request patterns.