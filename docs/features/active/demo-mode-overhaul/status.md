# Status — Demo Mode Overhaul

---

title: "Status - Demo Mode Overhaul"
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
- [ ] Audit of existing demo clients (AKS, Redis)
- [ ] Backend implementation (new demo clients: Service Bus, Storage, Releases)
- [ ] Frontend implementation (UX overhaul, banner)
- [ ] DI wiring for all demo clients
- [ ] Demo state persistence
- [ ] Tests (unit / manual)
- [ ] Docs aligned
- [ ] Ready for review

## Completed

- Feature scoped in `index.md`

## Remaining

- Author `backend.md` with demo client specs per area
- Author `frontend.md` with banner/toggle UX design
- Author `test-plan.md`
- Audit `DemoAksClient` against full current `IAksClient` interface (fill gaps)
- Audit `DemoRedisClient` against full current `IRedisClient` interface (fill gaps)
- Implement `DemoServiceBusClient` (namespaces, queues, topics, messages, DLQ, scheduled)
- Implement `DemoStorageClient` (accounts, containers, blobs, content)
- Implement `DemoReleasesClient` (pipelines, approvals, deployments)
- Update `MauiProgram.cs` to wire all demo clients when `UseDemoData = true`
- Replace checkbox in `TopBar.razor` with deliberate toggle + confirmation popover
- Add full-width amber demo banner in `MainLayout.razor`
- Persist demo state in `UiStateRepository`
- Handle DI switch while page is loaded (reload or state reset)
- Manual walkthrough of every feature area in demo mode

## Blockers

None.

## Validation

Not started.
