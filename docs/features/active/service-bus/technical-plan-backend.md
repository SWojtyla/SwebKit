---
title: "Technical Plan â€” Service Bus: Backend"
owner: ""
status: "In Progress"
created: "2026-03-08"
updated: ""
---

# Technical Plan â€” Service Bus: Backend

## Status

- Global namespace model + storage: **Done**
- Connection-string-only add flow: **Done**
- Entity listing (queues / topics / subscriptions): **Done**
- Project entity linking (SbEntityLink): **Done**
- DLQ batch operations (receive + complete/resubmit): **Done**
- Message send API (`SendMessageAsync`): **Done**
- Template domain model + persistence: **Done**
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

## Key types

- `ServiceBusNamespace` â€” Global namespace entry (alias, FQNS, credential key)
- `SbEntityLink` â€” Pins a queue/subscription to a project environment
- `SbEntityInfo` â€” Queue/topic/subscription descriptor with stats
- `SbMessage` â€” Mapped message for peek / send
- `IServiceBusClient` â€” Client contract
- `AzureServiceBusClient` â€” Azure SDK implementation

## Implementation Sequence

1. Add `ServiceBusNamespace` and `SbEntityLink` domain models.
2. Extend `ProfileRepository` with namespace CRUD.
3. Add primary `(string connectionString)` ctor to `AzureServiceBusClient`.
4. Implement `ListQueuesAsync` / `ListTopicsAsync` / `ListSubscriptionsAsync`.
5. Implement DLQ batch select â€” receive-and-complete / receive-and-resubmit with progress.
6. Implement `SendMessageAsync` / `SendBatchAsync` and add integration tests.
7. Add message template domain model and `ProfileRepository` CRUD.
8. Add scenario model, runner service, and cancellation support.
9. Validate production safety guards for all mutative operations.

## Detailed Tasks

- [x] Implement DLQ multi-receive with sequence-number targeting.
- [x] Add batch progress reporting via task queue.
- [x] Add message template domain model and persistence.
- [ ] Add scenario model and orchestrator.
- [ ] Enforce production env check before any mutative `IServiceBusClient` call.

## Acceptance Checks

- [x] Global namespaces added by connection string; FQNS auto-extracted.
- [x] Multiple namespaces stored and reloaded across restarts.
- [x] Queues, topics, and subscriptions listed with live counts.
- [x] Entity pins persisted per project environment.
- [x] Batch DLQ resubmit processes correct messages and reports progress via TaskQueue.
- [x] Send API delivers messages with custom properties.
- [x] Templates persist and reload without data loss.
- [ ] Scenario runner executes tasks sequentially and supports cancellation.

## Traceability Backlinks

- `docs/features/active/service-bus/index.md`
- `docs/features/active/service-bus/technical-plan-ui.md`
- `docs/features/active/service-bus/test-plan.md`

