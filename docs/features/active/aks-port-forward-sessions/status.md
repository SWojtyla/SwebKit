# Status — AKS Port-Forward Sessions Panel

---

title: "Status - AKS Port-Forward Sessions Panel"
owner: ""
state: "Planned"
branch: ""
started: ""
last_updated: "2026-03-21"

---

## Quick summary

Current state: Planned — feature scoped, awaiting implementation start.

## Progress checklist

- [x] Planning complete
- [ ] Design reviewed
- [ ] Backend implementation (`IPortForwardSessionService`)
- [ ] Frontend implementation (sessions panel, port-forward dialog)
- [ ] Status bar integration
- [ ] App exit cleanup
- [ ] Tests (unit / manual)
- [ ] Docs aligned (`aks.md` updated)
- [ ] Ready for review

## Completed

- Feature scoped in `index.md`

## Remaining

- Author `backend.md` with service design and process lifetime management
- Author `frontend.md` with panel component breakdown
- Author `test-plan.md`
- Implement `IPortForwardSessionService` + `PortForwardSessionService`
- Update `IAksClient.PortForwardAsync` to accept `CancellationToken` and return process handle
- Implement port-forward start dialog (local port input)
- Implement sessions panel in `AksPage.razor`
- Wire session count to status bar
- Wire app-exit cleanup
- Error state handling (process exits unexpectedly)
- Update `docs/architecture/functionalities/aks.md`

## Blockers

None.

## Validation

Not started.
