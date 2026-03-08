# Technical Plan — Service Bus: Backend

## Status

- Global namespace model + storage: **Done**
- Connection-string-only add flow: **Done**
- Entity listing (queues / topics / subscriptions): **Done**
- Project entity linking (SbEntityLink): **Done**
- DLQ batch operations (receive + complete/resubmit): **Done** (AzureServiceBusClient + DlqView batch bar)
- Message send API (`SendMessageAsync`): **Done** (AzureServiceBusClient + MessageComposer)
- Template domain model + persistence: **Done** (SbMessageTemplate, ProfileRepository, AppStateService)
- Scenario runner: Pending

## Architecture

```
ProfileData
  ServiceBusNamespaces: List<ServiceBusNamespace>   // global, stored in profiles.json

ProjectEnvironment
  ServiceBusEntityLinks: List<SbEntityLink>          // per-env pins

AzureServiceBusClient(string connectionString)       // primary ctor
AzureServiceBusClient(ServiceBusConfig, ICredentialStore)  // legacy ctor
```

### Key types

| Type | Location | Role |
|------|----------|------|
| `ServiceBusNamespace` | `SwebKit.Core/Domain/` | Global namespace entry (alias, FQNS, credential key) |
| `SbEntityLink` | `SwebKit.Core/Domain/` | Pins a queue/subscription to a project environment |
| `SbEntityInfo` | `SwebKit.Core/Models/` | Queue/topic/subscription descriptor with stats |
| `SbMessage` | `SwebKit.Core/Models/` | Mapped message for peek / send |
| `IServiceBusClient` | `SwebKit.Core/Abstractions/` | Client contract |
| `AzureServiceBusClient` | `SwebKit.Azure/ServiceBus/` | Azure SDK implementation |

### Connection string handling

- `ServiceBusConnectionStringProperties.Parse` extracts FQNS and optional entity path.
- Entity-scoped keys set `_scopedEntityPath`; when listing returns empty the scoped entity is used as fallback.
- Credentials are stored in Windows Credential Manager via `ICredentialStore` under the key `sb:ns:{guid}`.

## Implementation Sequence

1. ~~Add `ServiceBusNamespace` and `SbEntityLink` domain models.~~ **Done**
2. ~~Extend `ProfileRepository` with namespace CRUD.~~ **Done**
3. ~~Add primary `(string connectionString)` ctor to `AzureServiceBusClient`.~~ **Done**
4. ~~Implement `ListQueuesAsync` / `ListTopicsAsync` / `ListSubscriptionsAsync`.~~ **Done**
5. Implement DLQ batch select — receive-and-complete / receive-and-resubmit with progress.
6. Implement `SendMessageAsync` / `SendBatchAsync` (already drafted; needs integration tests).
7. Add message template domain model and `ProfileRepository` CRUD.
8. Add scenario model, runner service, and cancellation support.
9. Validate production safety guards for all mutative operations.

## Detailed Tasks

- [x] Implement DLQ multi-receive with sequence-number targeting.
  - Files: `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`
  - Done: `ResubmitDeadLetterAsync` and `CompleteDeadLetterAsync` accept a list of sequence number strings; DlqView batch bar drives batch calls.
- [x] Add batch progress reporting via task queue.
  - Files: `src/SwebKit.Core/Abstractions/ITaskQueue.cs`, `src/SwebKit.Core/Services/TaskQueueService.cs`
  - Done: `TaskQueue.Enqueue/Complete/Update` used for both single-message and batch DLQ operations.
- [x] Add message template domain model and persistence.
  - Files: `src/SwebKit.Core/Domain/SbMessageTemplate.cs` (new), `src/SwebKit.Core/Configuration/ProfileRepository.cs`
  - Done: `SbMessageTemplate`, `ProfileData.MessageTemplates`, `ProfileRepository.SaveMessageTemplate/DeleteMessageTemplate`, `AppStateService.SaveMessageTemplateAsync/DeleteMessageTemplateAsync`.
- [ ] Add scenario model and orchestrator.
  - Files: `src/SwebKit.Core/Domain/SbScenario.cs`, `src/SwebKit.Core/Services/SbScenarioRunner.cs`
- [ ] Enforce production env check before any mutative `IServiceBusClient` call.
  - Files: `src/SwebKit.Core/Abstractions/IServiceBusClient.cs`
  - Note: Currently enforced at UI layer via `ConfirmDialog` in `DlqView` and `MessageComposer`.

## Acceptance Checks

- [x] Global namespaces added by connection string; FQNS auto-extracted.
- [x] Multiple namespaces stored and reloaded across restarts.
- [x] Queues, topics, and subscriptions listed with live counts.
- [x] Entity pins persisted per project environment.
- [x] Batch DLQ resubmit processes correct messages and reports progress via TaskQueue.
- [x] Send API delivers messages with custom properties (MessageComposer → SendMessageAsync).
- [x] Templates persist and reload without data loss (saved to profiles.json, reloaded on app start).
- [ ] Scenario runner executes tasks sequentially and supports cancellation.

## Traceability Backlinks

- `docs/features/service-bus/index.md`
- `docs/features/service-bus/technical-plan-ui.md`
- `docs/features/service-bus/test-plan.md`
