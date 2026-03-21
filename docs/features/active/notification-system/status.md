# Status — Global Notification System

---

title: "Status - Global Notification System"
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
- [ ] Backend implementation (`INotificationService`)
- [ ] Frontend implementation (`NotificationToast.razor`, history panel)
- [ ] Integration across all feature pages
- [ ] Tests (unit / manual)
- [ ] Docs aligned
- [ ] Ready for review

## Completed

- Feature scoped in `index.md`

## Remaining

- Author `backend.md` with service API
- Author `frontend.md` with toast component design and animation
- Author `test-plan.md`
- Implement `INotificationService` + `NotificationService` (thread-safe, singleton)
- Register in `MauiProgram.cs`
- Implement `NotificationToast.razor` (stacked, auto-dismiss, slide-in animation)
- Add bell icon + history panel to `TopBar.razor`
- Wire `MainLayout.razor` to host the toast component
- Integrate into Service Bus feature (message sent, resubmitted, scheduled)
- Integrate into AKS feature (restarted, deleted, port-forward started/stopped)
- Integrate into Redis feature (key deleted, TTL updated, value saved, DB flushed)
- Integrate into Storage feature (downloaded, SAS copied)
- Integrate into Releases feature (approval submitted, deployment triggered)
- Migrate / deprecate inline `ErrorCallout` usages incrementally

## Blockers

None.

## Validation

Not started.
