# Status — Demo Mode Overhaul

---

title: "Status - Demo Mode Overhaul"
owner: ""
state: "In Progress"
branch: "main"
started: "2026-03-21"
last_updated: "2026-03-21"

---

## Quick summary

Current state: In Progress — core implementation complete. All demo clients implemented and wired. UX (banner + confirmation popover) done. Demo state persisted. AKS and Redis clients audited (full coverage confirmed). Remaining: manual validation across all feature areas.

## Progress checklist

- [x] Planning complete
- [x] Design reviewed
- [x] Audit of existing demo clients (AKS, Redis)
- [x] Backend implementation (new demo clients: Service Bus, Storage)
- [ ] Backend implementation (Releases / DevOps — `DemoDevOpsClient` already existed, no gaps found)
- [x] Frontend implementation (UX overhaul, banner)
- [x] DI wiring for all demo clients
- [x] Demo state persistence
- [ ] Tests (unit / manual)
- [ ] Docs aligned
- [ ] Ready for review

## Completed

- `DemoServiceBusClient` created in `src/SwebKit.Core/Services/` — extracts and supersedes `FakeServiceBusClient` from `ServiceBusPage.razor`. Two named constructors: `OrdersDev()` and `PaymentsDev()`, each with full namespace data including queues, topics, subscriptions, DLQ messages, and scheduled messages.
- `DemoStorageClient` created in `src/SwebKit.Core/Services/` — implements `IStorageClient` with 2 synthetic accounts, 3 containers, realistic blobs with JSON/CSV/text content.
- `DemoDevOpsClient` (Releases) already existed and fully covers `IDevOpsClient` — no changes needed.
- `DemoAksClient` audited against current `IAksClient` — all methods covered.
- `DemoRedisClient` audited against current `IRedisClient` — all methods covered.
- `UiState.UseDemoData` field added for persistence across restarts.
- `AppStateService.SetDemoModeAsync(bool)` added — saves to `UiStateRepository` and raises `DemoModeChanged` event.
- `AppStateService.InitializeAsync()` loads `UseDemoData` from persisted state.
- `DemoStorageClient` registered as singleton in `MauiProgram.cs`.
- `ServiceBusPage.razor` — removed inline `FakeServiceBusClient`, now uses `DemoServiceBusClient.OrdersDev()` and `DemoServiceBusClient.PaymentsDev()` (2 demo namespaces instead of 1).
- `StoragePage.razor` — `RebuildClient()` injects `DemoStorageClient` when `AppState.UseDemoData` is true.
- `TopBar.razor` — replaced plain checkbox with deliberate demo button + confirmation popover (amber "Enable" action, Cancel button).
- `MainLayout.razor` — amber demo banner rendered below top bar when `UseDemoData` is true; "Disable" button calls `SetDemoModeAsync(false)` and navigates to `/`; subscribes to `AppState.DemoModeChanged` for reactive re-render.
- `app.css` — grid template updated to 4 rows with `auto` banner row (collapses to 0 when inactive); `.demo-banner` and `.demo-banner-disable` styles added.

## Remaining

- Manual walkthrough of every feature area in demo mode
- Unit tests: all new demo clients implement interface without throwing

## Blockers

None.

## Validation

Not started.
