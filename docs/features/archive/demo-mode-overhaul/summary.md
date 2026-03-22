# Archive Summary - Demo Mode Overhaul

---

title: "Archive Summary - Demo Mode Overhaul"
owner: ""
completed_date: "2026-03-22"
pr: ""
commit: ""

---

## Goal

Replace the hidden demo checkbox in `TopBar` with a deliberate, visible demo mode indicator and extend demo data coverage to all feature areas (Service Bus, Storage, Releases/DevOps) that previously lacked it, so the application is fully usable for presentations, onboarding, and offline development without any live Azure or Kubernetes connections.

## Delivered

- **Demo mode UX** — plain checkbox replaced with a "Demo" button in the top bar that opens a confirmation popover (amber "Enable" / "Cancel"). An amber full-width banner renders below the top bar when active with a "Disable" button. Disabling navigates to `/` to force page re-initialisation.
- **`DemoServiceBusClient`** — implements `IServiceBusClient` with two synthetic namespaces (`orders-dev`, `payments-dev`), each with queues, topics, subscriptions, active messages (varied JSON payloads), DLQ messages, and scheduled messages. Supersedes the inline `FakeServiceBusClient` that was embedded in `ServiceBusPage`.
- **`DemoStorageClient`** — implements `IStorageClient` with two synthetic accounts, three containers each, and realistic blobs (JSON, CSV, text) with content preview support.
- **`DemoDevOpsClient` fixes** — corrected all pipeline runs for pipelines 101, 103, and 201 to return `"inProgress"` state on their latest run so that `ApprovalCenter` and the approval badge actually display pending approvals. Added `GetWaitingStagesAsync` entries for all three pipelines. Added `DemoReleases` static property — two pre-built `ReleaseRecord` objects referencing the demo projects/pipelines.
- **`ReleasesPage` demo support** — `EffectiveReleases` always returns `DemoDevOpsClient.DemoReleases` in demo mode so the release board, approval center, and tag manager show realistic synthetic content rather than the user's real releases. Demo-only releases suppress the Edit/Delete buttons.
- **Delete release** — added a Delete button (with confirmation dialog) accessible on any persisted release.
- **Demo state persistence** — `UseDemoData` persisted in `UiStateRepository` JSON so demo mode survives app restarts.
- **`AppStateService.SetDemoModeAsync`** — saves to `UiStateRepository` and fires `DemoModeChanged` event.
- **`TopBar` reactivity fix** — subscribed `TopBar` to `DemoModeChanged` so the "Demo" button reappears immediately after disabling, without requiring a re-render from another source.
- **`DemoAksClient` and `DemoRedisClient`** — audited against current `IAksClient` / `IRedisClient`; full coverage confirmed.

## Key decisions

- **D-1: Page-level instantiation over DI factory** — Demo clients are injected as singletons and selected in page code (`ActiveClient =>`) rather than using a factory at DI resolution time. Avoids rewriting the runtime-configured client registration pattern used throughout the codebase.
- **D-2: Two demo Service Bus namespaces** — Exercises the multi-namespace selector UI and gives a more realistic demo story than a single generic namespace.
- **D-3: `DemoModeChanged` event** — `AppStateService` fires an `Action?` event from `SetDemoModeAsync`; layout and top bar subscribe to it for immediate reactive re-render without polling.
- **D-4: Navigate to `/` on disable** — Prevents stale demo-client references in feature pages after the mode switches. All pages re-initialise on navigation.

## Validation performed

- `dotnet build` on `SwebKit.Core` and `SwebKit.App` (Windows target) — 0 errors throughout.
- `dotnet test tests/SwebKit.DevOps.Tests` — 24/24 passed.
- Manual walkthrough: demo mode enable/disable cycle (banner appears, "Demo" button reappears after disable), Releases page shows demo releases with board data and approval badge, Approval Center shows pending cards.

## Lessons learned

- **GUID strings must be all-hex** — `new Guid("d3m0beef-...")` throws `FormatException` at type initialisation; use only `[0-9a-f]`. Always verify demo GUIDs with a hex check.
- **Event subscription for singleton-state UI** — Any Blazor component that conditionally renders based on a singleton service's state (e.g. `AppState.UseDemoData`) must subscribe to the service's change event. Missing subscriptions produce confusing "button disappears forever" bugs.
- **`inProgress` is the gate for approvals** — `ApprovalCenter` and the approval badge both skip runs where `State == "completed"`. Demo pipeline runs must return `"inProgress"` for the latest run if approval visibility is desired.

## Follow-up

- Unit tests for `DemoServiceBusClient`, `DemoStorageClient`, and the fixed `DemoDevOpsClient` behaviours (inProgress runs, waiting stages) — not written yet.
- A toast or notification explaining the navigation-on-disable would improve UX (referenced in D-4).

## Archive metadata

- Active folder: `docs/features/active/demo-mode-overhaul/` (deleted on archive)
- Related source: `src/SwebKit.Core/Services/Demo*.cs`, `src/SwebKit.App/Components/Layout/TopBar.razor`, `src/SwebKit.App/Components/Layout/MainLayout.razor`, `src/SwebKit.App/Components/Pages/ReleasesPage.razor`, `src/SwebKit.App/MauiProgram.cs`
