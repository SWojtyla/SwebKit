# Technical Plan - Service Bus

## Status

- Global namespace model + storage: **Done**
- Connection-string-only add flow: **Done**
- Multi-namespace left panel: **Done**
- Entity tree queues/topics/subscriptions: **Done**
- Project entity linking (pin/unpin): **Done**
- DLQ batch operations: Pending
- Message composer: Pending
- Templates + scenarios: Pending

## Architecture

```
ProfileData
  ServiceBusNamespaces: List<ServiceBusNamespace>   // global

ProjectEnvironment
  ServiceBusEntityLinks: List<SbEntityLink>          // per-env pins

AzureServiceBusClient(string connectionString)       // primary ctor
AzureServiceBusClient(ServiceBusConfig, ICredentialStore)  // legacy
```

## Implementation Sequence

1. ~~Add global `ServiceBusNamespace` domain model and storage in `ProfileRepository`.~~ **Done**
2. ~~Add `SbEntityLink` model and `ServiceBusEntityLinks` to `ProjectEnvironment`.~~ **Done**
3. ~~Rework `ServiceBusPage` for multi-namespace left panel.~~ **Done**
4. ~~Update `EntityTree` with namespace-aware pin/unpin.~~ **Done**
5. ~~Add connection-string-only constructor to `AzureServiceBusClient`.~~ **Done**
6. ~~Update `SettingsPage` and `ProjectEditDialog` for new model.~~ **Done**
7. Implement DLQ multi-select and batch operation framework.
8. Implement message composer with payload and property editors.
9. Add template persistence and management UX.
10. Add scenario orchestration with task-queue progress.
11. Add favorites, live counters, and auto-refresh.
12. Add filter-state persistence and advanced SQL mode.
13. Add export and clipboard workflows.
14. Validate production safety behavior across mutative actions.

## Detailed Tasks

- [ ] Implement DLQ batch selection and action bar.
  - Files: `src/SwebKit.App/Components/ServiceBus/DlqView.razor`
- [ ] Implement batch progress reporting in task queue.
  - Files: `src/SwebKit.Core/Abstractions/ITaskQueue.cs`, `src/SwebKit.Core/Services/TaskQueueService.cs`
- [ ] Build message composer UI and entity target selector.
  - Files: `src/SwebKit.App/Components/ServiceBus/MessageComposer.razor`
- [ ] Add message template model and persistence.
  - Files: `src/SwebKit.Core/Domain/*`, `src/SwebKit.Core/Configuration/ProfileRepository.cs`
- [ ] Build template picker and management actions.
  - Files: `src/SwebKit.App/Components/ServiceBus/TemplatePicker.razor`
- [ ] Add scenario model and scenario runner orchestration.
  - Files: `src/SwebKit.Core/Domain/*`, `src/SwebKit.App/Components/ServiceBus/ScenarioEditor.razor`
- [ ] Add auto-refresh controls and visibility-aware pause.
  - Files: `src/SwebKit.App/Components/ServiceBus/*`
- [ ] Persist and restore filter state by entity path.
  - Files: `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- [ ] Add export and copy actions for rows and bodies.
  - Files: `src/SwebKit.App/Components/ServiceBus/*`
- [ ] Enforce production confirm dialogs for mutative actions.
  - Files: `src/SwebKit.App/Components/Shared/ConfirmDialog.razor`

## Acceptance Checks

- [x] Global namespaces added by connection string only.
- [x] Multiple namespaces shown simultaneously in left panel.
- [x] Queues and topics shown after connection; subscriptions on expand.
- [x] Pin/unpin entity to project environment (📍/📌 icon).
- [ ] Batch DLQ resubmit and complete work with progress.
- [ ] Composer sends messages with custom properties.
- [ ] Templates save, load, and execute correctly.
- [ ] Scenarios run sequentially and are cancellable.
- [ ] Auto-refresh and filter persistence behave as expected.
- [ ] Production safety gates are consistently enforced.

## Traceability Backlinks

- `docs/features/service-bus/index.md`
- `docs/features/service-bus/test-plan.md`
- `docs/plans/docs-rework-traceability/index.md`
