# Status - AKS Enhancements

---

title: "Status - AKS Enhancements"
owner: ""
state: "Planned"
branch: ""
started: "2026-03-10"
last_updated: "2026-03-10"

---

## Quick summary

Follow-up feature opened after archiving AKS connectivity foundation. Focus is now full namespace-scoped resource browsing and YAML inspection.

**Current focus:** Implement kubeconfig context discovery and namespace/resource contracts.

## Progress checklist

- [x] Planning complete
- [ ] Design reviewed
- [ ] Backend implementation
- [ ] Frontend implementation
- [ ] Tests (unit/integration/e2e)
- [x] Docs aligned
- [ ] Ready for review

## Completed

- Created follow-up feature scope and module docs.
- Linked this feature to archived AKS connectivity foundation docs.

## Remaining

- Implement context discovery and selector wiring.
- Implement namespace/pods/deployments/helm/ingress browse flows.
- Implement read-only YAML viewer for supported kinds.
- Expand tests and complete manual validation.

## Blockers

- None.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- Keep all AKS operations read-only in this phase.
