# Status — AKS New Capabilities

---

title: "Status - AKS New Capabilities"
owner: ""
state: "Planned"
branch: ""
started: ""
last_updated: "2026-03-15"

---

## Quick summary

Current state: Planned — full backend and frontend plans written, ready to start implementation.

## Progress checklist

- [x] Planning complete
- [ ] Design reviewed
- [ ] Backend implementation
- [ ] Frontend implementation
- [ ] Tests (unit / integration / manual)
- [ ] Docs aligned
- [ ] Ready for review

## Completed

- Feature scoping and planning
- `backend.md`, `frontend.md`, `test-plan.md`, `decisions.md` authored

## Remaining

- Implement new model types in `AksModels.cs`
- Extend `IAksClient` with 9 new method signatures
- Implement stubs in `DemoAksClient`
- Feature 2: StatefulSets tab (backend + UI)
- Feature 3: ConfigMap and Secret viewer (backend + UI)
- Feature 5: HPA inline status (backend + UI)
- Feature 6: Open shell in pod (UI only, reuses existing method)
- Feature 4: Container image and env vars quick-view (backend + UI)
- Feature 1: Multi-pod log aggregation (backend + UI)
- Unit tests for new backend methods
- Manual validation per `test-plan.md`
- Update `docs/architecture/functionalities/aks.md`

## Blockers

None.

## Validation

Not started. See `test-plan.md`.
