# Service Bus

## Purpose

Provide a global Service Bus workspace for .NET developers: add namespaces by connection string,
inspect queues/topics/subscriptions, manage DLQ messages, and pin specific entities to projects.

## Scope

- Global namespace registry (connection string only, not per-project)
- Multiple namespaces simultaneously visible in the left panel
- Entity tree per namespace: queues, topics, subscriptions with live message counts
- Pin/unpin queues or subscriptions to project environments (📍 icon)
- Tab-based message inspector and DLQ view per selected entity
- DLQ batch operations and safety confirmations
- Message composer and send flows
- Message templates and scenario execution
- Favorites, live counts, auto-refresh, and filter persistence
- Export and advanced filtering support

## Key Design Decisions (updated 2026-03-08, bugs fixed 2026-03-08)

- **Namespaces are global** — stored in `ProfileData.ServiceBusNamespaces`, not inside `ProjectEnvironment`.
- **Connection string only** — the add flow parses the FQNS automatically; no hostname or auth-mode fields.
- **Project entity links** — `ProjectEnvironment.ServiceBusEntityLinks` (list of `SbEntityLink`) stores which queues/subscriptions are pinned to an environment.
- `AzureServiceBusClient` has a primary `(string connectionString)` constructor; the legacy `(ServiceBusConfig, ICredentialStore)` constructor is kept for backward compatibility.
- **Component namespace imports** — all sub-folder component namespaces (`ServiceBus`, `Aks`, `Layout`) must be explicitly imported in `_Imports.razor`; missing imports cause Blazor to silently treat components as unknown HTML elements (no lifecycle, no render).
- **`InvokeAsync(StateHasChanged)`** must be used inside async lifecycle methods (e.g. after `await` in `LoadAsync`) to safely dispatch re-renders in MAUI Hybrid.
- **Demo namespace** — a `FakeServiceBusClient` is wired to a "Demo" button in the namespace panel, providing hardcoded queues/topics/subscriptions for UI smoke-testing without a real Azure connection.
- **EntityTree concurrent-load guard** — `_loadedClient` is set before `await LoadAsync()` in `OnParametersSetAsync` to prevent concurrent loads when the parent re-renders during an async load.

## Logical Outcome

A practical Service Bus operations workspace that supports fast and safe debugging across
multiple namespaces, with project-scoped entity filtering and guarded mutation in prod.

## Dependencies

- Depends on `docs/features/foundation-mvp/`

## Source Traceability

- Canonical feature scope: `docs/features/service-bus/index.md`
- Supporting context: `docs/ARCHITECTURE.md`, `docs/DESIGN.md`

## Deliverables

- `docs/features/service-bus/technical-plan-backend.md`
- `docs/features/service-bus/technical-plan-ui.md`
- `docs/features/service-bus/technical-plan-ui-bugfixes.md`
- `docs/features/service-bus/test-plan.md`

## Migration Notes

`ServiceBusConfig` has been removed from `ProjectEnvironment`. Existing profiles lose any stored
per-environment SB hostname. Users re-add namespaces via the global namespace panel (connection string).
