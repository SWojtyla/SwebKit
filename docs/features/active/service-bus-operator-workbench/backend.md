# Backend Plan - service-bus-operator-workbench

---

title: "Backend Plan - service-bus-operator-workbench"
owner: "GitHub Copilot"
status: "Planned"

---

## Goal

Extend the existing Service Bus contracts and client implementation so the UI can surface richer message metadata, inspect sessionized workloads, and execute preview-first bulk send or replay operations without changing the app into a background message processor.

## Impacted areas

- Existing models and config:
- `src/SwebKit.Core/Models/ServiceBusModels.cs`
- `src/SwebKit.Core/Domain/SbMessageTemplate.cs`
- `src/SwebKit.Core/Domain/ServiceBusNamespace.cs`
- Existing abstractions and services:
- `src/SwebKit.Core/Abstractions/IServiceBusClient.cs`
- `src/SwebKit.App/Services/ServiceBusNamespaceBootstrapper.cs`
- Existing Azure implementation:
- `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`
- `src/SwebKit.Azure/ServiceBus/DeadLetterSequenceProcessor.cs`
- Existing downstream integration point:
- `src/SwebKit.Azure/ServiceBus/IncidentTimeline/ServiceBusEvidenceSignalSource.cs`
- Planned new or additive contracts:
- `SbSessionInfo`, `SbTraceReference`, and `SbBatchOperationPreview` in `ServiceBusModels.cs`
- Additive methods on `IServiceBusClient` for session enumeration or bounded session peek where the SDK makes that feasible
- Optional helper service for trace-key extraction and replay preview normalization in `src/SwebKit.Core/Services`

## Design

The workbench should stay explicit and bounded:

1. Reuse existing `SbMessage` and `SbSystemProperties` fields as the first source of richer triage metadata.
2. Add new session-summary or trace-reference contracts only where the current models cannot express the operator need cleanly.
3. Keep session and trace queries on-demand. No hidden background receivers, lock renewers, or indefinite listeners.
4. Reuse current send and replay operations where possible. New preview contracts should be built around `SendBatchAsync`, `ResubmitDeadLetterAsync`, and existing `RemapRules`, not around a separate mutation engine.
5. Treat downstream investigation handoff as a secondary contract. The primary backend responsibility is to normalize explicit message evidence and batch-operation intent safely.

## API / Contracts

- Existing fields already available for UI surfacing:
- `SbMessage.DeadLetterReason`
- `SbMessage.DeadLetterErrorDescription`
- `SbMessage.SessionId`
- `SbSystemProperties.ExpiresAt`
- `SbSystemProperties.PartitionKey`
- Likely additive contracts:
- `SbSessionInfo` with session ID, message counts, earliest and latest message timestamps, and maybe lock-related or state summary when available.
- `SbTraceReference` with source key type, source value, explanation text, and downstream destinations.
- `SbBatchOperationPreview` with message count, target entity, remap summary, environment summary, and validation issues.
- Likely additive client methods:
- A bounded session listing or session-summary method.
- A bounded message peek by session where supported.
- Optional metadata retrieval helpers if replay or send preview needs broker-side validation.
- Backward compatibility:
- Existing `PeekMessagesAsync`, `PeekDeadLetterAsync`, `SendBatchAsync`, `ResubmitDeadLetterAsync`, and `CompleteDeadLetterAsync` remain valid.
- New contracts must be additive so current Service Bus views continue to work unchanged while the workbench is built out.

## Tasks

### Wave 1 - richer metadata and session contracts [dotnet-expert]

- [ ] Audit which desired UI fields already exist in `ServiceBusModels.cs` and which require additive contracts.
- [ ] Extend `IServiceBusClient` and `AzureServiceBusClient` for bounded session visibility where feasible.
- [ ] Add trace-reference normalization from message IDs, correlation IDs, and supported application-property keys.

### Wave 2 - batch preview and execution summaries [dotnet-expert]

- [ ] Define preview contracts for batch replay and batch send.
- [ ] Reuse `RemapRules` and existing send or replay methods rather than introducing a separate mutation engine.
- [ ] Return partial-success summaries explicitly when some items fail.

### Wave 3 - downstream handoff and performance hardening [dotnet-expert]

- [ ] Normalize message trace references for Incident Timeline or Observability handoff.
- [ ] Bound high-volume session and trace calls to keep interactive latency stable.
- [ ] Extend tests for scoped connection string and limited-claim behavior.

## Migration and runtime changes

- No storage migration is required.
- Batch send import can remain in-memory unless the existing template model proves insufficient.
- Session and trace features should degrade gracefully when the credential or entity type does not support the desired metadata.

## Validation

- Unit tests: Not started. Add trace-key and preview-normalization tests in `tests/SwebKit.Core.Tests`.
- Integration tests: Not started. Extend `tests/SwebKit.Azure.Tests` around Azure Service Bus parsing, session behavior, and replay summaries.
- Manual checks: verify explicit limitations on scoped entities and limited claims are surfaced instead of failing silently.

## Notes

- `azure-sdk.md` remains directly relevant. Enumerations should use bounded async patterns and dispose pageable enumerators safely.
- Batch operations must preserve current production safeguards and should not hide partial failure behind a single success toast.
