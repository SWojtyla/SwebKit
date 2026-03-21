# Feature Overview — Demo Mode Overhaul

---

title: "Demo Mode Overhaul"
owner: ""
status: "Planned"
created: "2026-03-21"
updated: "2026-03-21"

---

## Goal

Replace the hidden demo checkbox in the TopBar with a deliberate, visible demo mode indicator and extend demo data coverage to all feature areas that currently lack it, so the application is fully usable without any live Azure or Kubernetes connections.

## Value

Demo mode is currently easy to leave on by accident (a plain checkbox) and provides incomplete coverage (only AKS and Redis have demo clients). A visible banner makes the mode obvious and its activation explicit. Full demo coverage makes SwebKit usable for onboarding, presentations, UI development, and offline work across every feature.

## Scope

### In scope

1. **Demo mode activation UX** — Replace the plain `<input type="checkbox">` in `TopBar.razor` with a deliberate toggle. When demo mode is off, show a small "Demo" button/badge. Clicking it shows a confirmation popover: "Enable demo mode? Live connections will be replaced with synthetic data." with Enable / Cancel. When active, a prominent amber banner ("DEMO MODE — data is synthetic. No live connections are used.") is shown below the top bar, spanning the full width. The banner has a "Disable" button.

2. **Demo coverage audit and completion** — Audit all feature areas and implement missing demo clients:
   - **Service Bus** — `DemoServiceBusClient`: returns synthetic namespaces, queues, topics, subscriptions, and messages. Messages have varied bodies (JSON payloads), properties, and enqueue times. Dead-letter queue has pre-populated messages. Scheduled messages has 2–3 entries.
   - **Storage** — `DemoStorageClient`: returns 2 synthetic storage accounts, each with 3 containers and a mix of blobs (JSON, text, image placeholder). Blob content returns plausible sample JSON/text.
   - **Releases** — `DemoReleasesClient`: returns a pipeline board with 3 pipelines in mixed states (succeeded, running, awaiting approval). Approval center has 1 pending approval.
   - **AKS** — `DemoAksClient` already exists; verify full coverage against all current `IAksClient` methods (including methods added in v3/v4 enhancements).
   - **Redis** — `DemoRedisClient` already exists; verify coverage against all current `IRedisClient` methods.

3. **Demo client wiring** — Update `MauiProgram.cs` (or a dedicated demo-mode DI extension) so that when `AppState.UseDemoData` is true, all feature clients resolve to their demo implementations. Currently only AKS and Redis do this; Service Bus, Storage, and Releases must be included.

4. **Demo state persistence** — Persist the demo mode toggle state in `UiStateRepository` so it survives app restarts (useful for dedicated demo environments).

### Out of scope

- Realistic latency simulation in demo clients (immediate returns are acceptable)
- Demo data that matches the user's actual configured profile (demo data is always synthetic)
- A dedicated demo profile or project

## Dependencies

- `AppStateService.UseDemoData` — existing flag, already wired for AKS and Redis
- `MauiProgram.cs` — DI registration for demo clients
- `TopBar.razor` — UX change
- `MainLayout.razor` — banner render
- `UiStateRepository` — persist demo toggle
- New: `DemoServiceBusClient`, `DemoStorageClient`, `DemoReleasesClient`
- Existing: `DemoAksClient`, `DemoRedisClient` (audit and fill gaps)

## Risks

- Demo client completeness: if any `IAksClient` or `IRedisClient` methods are missing from demo implementations, runtime will throw `NotImplementedException` when those code paths are exercised. A thorough method-by-method audit is required before marking done.
- DI switch while running: if the user toggles demo mode while a page has active state (e.g. AKS page loaded with real data), the page must handle the transition gracefully — either re-initialise or show a "reload required" message.

## Related documents

- Architecture: `docs/architecture/architecture.md`, `docs/architecture/design.md`
- Pitfalls: `docs/pitfalls/blazor-maui.md`

## Quick links

- Status: `status.md`
- Backend plan: `backend.md`
- Frontend plan: `frontend.md`
- Test plan: `test-plan.md`
