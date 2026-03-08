# Backend Plan - Service Bus

---

title: "Backend Plan - Service Bus"
owner: ""
status: "In Progress"

---

## Goal

Deliver reliable backend primitives for global namespaces, entity listing, DLQ remediation, message send APIs, and template persistence.

## Impacted areas

- Projects / services: `src/SwebKit.Azure/ServiceBus/`, `src/SwebKit.Core/Domain/`, `src/SwebKit.Core/Configuration/`
- Storage: `profiles.json` (ProfileData)

## Design

Preserve the global namespace model (`ProfileData.ServiceBusNamespaces`) and per-environment entity pins (`ProjectEnvironment.ServiceBusEntityLinks`). The primary `AzureServiceBusClient(string connectionString)` ctor is the canonical path for runtime usage; legacy ctor retained for compatibility.

## Architecture

```
ProfileData
	ServiceBusNamespaces: List<ServiceBusNamespace>   // global, stored in profiles.json

ProjectEnvironment
	ServiceBusEntityLinks: List<SbEntityLink>          // per-env pins

AzureServiceBusClient(string connectionString)       // primary ctor
AzureServiceBusClient(ServiceBusConfig, ICredentialStore)  // legacy ctor
```

## Key types

- `ServiceBusNamespace` — Global namespace entry (alias, FQNS, credential key)
- `SbEntityLink` — Pins a queue/subscription to a project environment
- `SbEntityInfo` — Queue/topic/subscription descriptor with stats
- `SbMessage` — Mapped message for peek / send
- `IServiceBusClient` — Client contract
- `AzureServiceBusClient` — Azure SDK implementation

## Implementation Sequence

1. Add `ServiceBusNamespace` and `SbEntityLink` domain models.
2. Extend `ProfileRepository` with namespace CRUD.
3. Add primary `(string connectionString)` ctor to `AzureServiceBusClient`.
4. Implement `ListQueuesAsync` / `ListTopicsAsync` / `ListSubscriptionsAsync`.
5. Implement DLQ batch select — receive-and-complete / receive-and-resubmit with progress.
6. Implement `SendMessageAsync` / `SendBatchAsync` and add integration tests.
7. Add message template domain model and `ProfileRepository` CRUD.
8. Add scenario model, runner service, and cancellation support.
9. Validate production safety guards for all mutative operations.

## Acceptance Checks

- Global namespaces added by connection string; FQNS auto-extracted.
- Multiple namespaces stored and reloaded across restarts.
- Queues, topics, and subscriptions listed with live counts.
- Entity pins persisted per project environment.
- Batch DLQ resubmit processes correct messages and reports progress via TaskQueue.
- Send API delivers messages with custom properties.
- Templates persist and reload without data loss.
- Scenario runner executes tasks sequentially and supports cancellation (planned).

## API / Contracts

- `IServiceBusClient` surface for `ListQueuesAsync`, `ListTopicsAsync`, `ListSubscriptionsAsync`, `SendMessageAsync`, `ResubmitDeadLetterAsync`, `CompleteDeadLetterAsync`.
- Connection-string parser: `ServiceBusConnectionStringProperties.Parse` (extract FQNS and optional entity path).

## Tasks

- [x] Add `ServiceBusNamespace` and `SbEntityLink` domain models
- [x] Extend `ProfileRepository` with namespace CRUD
- [x] Add primary `(string connectionString)` ctor to `AzureServiceBusClient`
- [x] Implement list APIs for queues/topics/subscriptions
- [x] Implement DLQ multi-receive + batch operations
- [x] Implement message templates persistence
- [ ] Implement scenario model & runner
- [ ] Add integration tests for send/peek/resubmit flows

## Migration and runtime changes

- Remove `ServiceBusConfig` from `ProjectEnvironment`; re-add namespaces via global namespace panel.
- Credential storage via `ICredentialStore` (Windows Credential Manager) under `sb:ns:{guid}`.

## Validation

- Unit tests: in progress
- Integration tests: planned (mocked service)
- Manual: smoke tests for DLQ >1000 and multi-namespace flows

## Notes

- Enforce production confirmation at the UI layer and, where possible, guard mutative backend operations as defense-in-depth.
