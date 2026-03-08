# Service Bus

---

title: "Service Bus"
owner: ""
status: "Implemented (partial)"
created: ""
updated: "2026-03-08"

---

## Implementation status (snapshot)

| Area                                                            | Status     |
| --------------------------------------------------------------- | ---------- |
| Namespace panel (add / collapse / remove)                       | ✅ Done    |
| Entity tree (queues / topics / subscriptions + live counts)     | ✅ Done    |
| Tab system (open / close / DLQ vs Active mode)                  | ✅ Done    |
| Pin / unpin entities to project environments                    | ✅ Done    |
| Demo namespace (FakeServiceBusClient)                           | ✅ Done    |
| Bug fix pack SB-UI-BUG-01..04                                   | ✅ Done    |
| DLQ multi-select + batch resubmit / delete                      | ✅ Done    |
| Message composer (body + properties + send)                     | ✅ Done    |
| Message templates (save / load / delete)                        | ✅ Done    |
| Auto-refresh interval selector                                  | ✅ Done    |
| Copy Body + Copy Full Message export                            | ✅ Done    |
| Production ConfirmDialog for all mutative ops                   | ✅ Done    |
| UI polish: grid layout fix (headers/resize)                     | ✅ Done    |
| UI polish: button label clarity (Copy Full Message)             | ✅ Done    |
| UI polish: save message as template from detail pane            | ✅ Done    |
| UI polish: enhanced template management (rename/edit/duplicate) | ✅ Done    |
| UI polish: resizable splitter (list ↔ detail)                   | ✅ Done    |
| UI polish: sortable columns                                     | ✅ Done    |
| UI polish: empty state for message list                         | ✅ Done    |
| UI polish: keyboard navigation (arrow/escape)                   | ✅ Done    |
| UI polish: copy feedback toast                                  | ✅ Done    |
| Scenario editor + runner                                        | ⏳ Pending |
| Filter-state persistence by entity path                         | ⏳ Pending |

## Purpose

Provide a global Service Bus workspace for .NET developers: add namespaces by connection string, inspect queues/topics/subscriptions, manage DLQ messages, and pin specific entities to projects.

## Scope

- Global namespace registry (connection string only, not per-project)
- Multiple namespaces visible in the left panel
- Entity tree per namespace with live message counts
- Pin/unpin queues or subscriptions to project environments
- Tab-based message inspector and DLQ view per selected entity
- DLQ batch operations and safety confirmations
- Message composer and send flows
- Message templates and scenario execution
- Favorites, live counts, auto-refresh, and filter persistence
- Export and advanced filtering support

## Key design decisions

- Namespaces are global — stored in `ProfileData.ServiceBusNamespaces`.
- Connection string only — add flow parses FQNS automatically.
- Project entity links stored in `ProjectEnvironment.ServiceBusEntityLinks`.
- `AzureServiceBusClient` primary constructor is `(string connectionString)`; legacy constructor kept for compatibility.
- Component namespaces must be imported in `_Imports.razor` to avoid runtime issues.
- Use `InvokeAsync(StateHasChanged)` after awaited work in lifecycle methods to dispatch UI updates safely in MAUI Hybrid.
- Demo namespace via `FakeServiceBusClient` enables UI smoke-tests without Azure.
- Guard concurrent loads in `EntityTree` by setting `_loadedClient` before awaiting `LoadAsync()`.

## Logical outcome

A practical Service Bus operations workspace that supports fast and safe debugging across multiple namespaces, with project-scoped entity filtering and guarded mutation in prod.

## Dependencies

- Depends on `docs/features/active/foundation-mvp/`

## Source traceability

- Canonical feature scope: `docs/features/active/service-bus/index.md`
- Supporting context: `docs/ARCHITECTURE.md`, `docs/DESIGN.md`

## Deliverables

- `docs/features/active/service-bus/backend.md`
- `docs/features/active/service-bus/frontend.md`
- `docs/features/active/service-bus/decisions.md`
- `docs/features/active/service-bus/status.md`
- `docs/features/active/service-bus/test-plan.md`

## Migration notes

`ServiceBusConfig` has been removed from `ProjectEnvironment`; users re-add namespaces via the global namespace panel.
