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

Key types and architecture are documented in `technical-plan-backend.md`.

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
