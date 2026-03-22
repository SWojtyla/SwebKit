# Decisions — Demo Mode Overhaul

## D-1 — DI strategy: page-level instantiation instead of factory pattern

**Decision:** Demo clients are instantiated directly in page code (`new DemoAksClient()`, `DemoServiceBusClient.OrdersDev()`, `DemoStorageClient` via injection) rather than using a DI factory that switches at resolution time.

**Rationale:** The existing codebase already uses this pattern for AKS and Releases pages. A DI factory (Option A from `backend.md`) would require all feature clients to be registered as `IServiceBusClient`, `IStorageClient`, etc., which conflicts with the current design where clients are created from user configuration at runtime (connection strings from `ICredentialStore`, etc.). Page-level instantiation avoids DI overhaul while keeping the toggling behaviour straightforward.

**How to apply:** When adding a new feature area, check if its page already creates its client directly. If so, add a demo branch in `RebuildClient()` or `OnInitialized()`. If not, inject the demo client as a singleton and switch there.

---

## D-2 — Two demo namespaces for Service Bus instead of one

**Decision:** The demo mode exposes two Service Bus namespaces (`orders-dev` and `payments-dev`) rather than the single generic `demo-namespace` that `FakeServiceBusClient` used.

**Rationale:** Two namespaces better exercises the multi-namespace UI, makes the namespace selector non-trivial to test, and provides a more realistic demo story. Each namespace has its own set of queues, topics, and message data.

---

## D-3 — `DemoModeChanged` event on `AppStateService`

**Decision:** `AppStateService` exposes a `DemoModeChanged: event Action?` that fires when `SetDemoModeAsync` is called. `MainLayout` subscribes to re-render the banner.

**Rationale:** `AppStateService` is a singleton injected into both `MainLayout` and feature pages. Without a change event, the banner would only update on the next Blazor render cycle triggered by something else. The event ensures immediate, predictable reactivity without polling.

---

## D-4 — Navigate to `/` on demo mode disable

**Decision:** Disabling demo mode navigates the user back to the dashboard (`/`).

**Rationale:** Feature pages hold live client references. When demo mode is disabled, those references point to demo clients that no longer match the new mode. Navigation to `/` forces all pages to re-initialise with real clients, preventing stale state bugs. A notification (future work) could be shown to explain the navigation.
