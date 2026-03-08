---
title: "Technical Plan â€” Foundation MVP: Backend"
owner: ""
status: "In Progress"
created: "2026-03-08"
updated: ""
---

# Technical Plan â€” Foundation MVP: Backend

## Status

- Current: In Progress

## Architecture

```
SwebKit.Core
  Domain/          â€” Project, ProjectEnvironment, ServiceBusNamespace, SbEntityLink
  Abstractions/    â€” IServiceBusClient, IObservabilityProvider, IAksClient, ICredentialStore, IAppEventBus
  Configuration/   â€” ProfileRepository, UiStateRepository, AppDataPaths
  Services/        â€” AppStateService, AppEventBus

SwebKit.Azure
  ServiceBus/      â€” AzureServiceBusClient
  Observability/   â€” AppInsightsObservabilityProvider

SwebKit.Kubernetes
  AksClient/       â€” KubernetesAksClient

SwebKit.OpenTelemetry
  OtlpObservabilityProvider
```

## Implementation Sequence

1. Validate solution scaffold and project references.
2. Finalize core domain and abstraction contracts.
3. Finalize profile, UI-state, and credential storage.
4. Complete service client implementations.
5. Wire DI in `MauiProgram.cs`.
6. Add baseline tests for core services and client guards.

## Detailed Tasks

- [ ] Confirm all projects build from clean clone.
- [ ] Confirm domain types align with design contract.
- [ ] Confirm interface signatures are stable for downstream features.
- [ ] Validate persistence models for profiles and UI state.
- [ ] Validate credential storage abstraction and Windows implementation.
- [ ] Complete and verify Service Bus client integration.
- [ ] Complete and verify App Insights provider integration.
- [ ] Complete and verify Kubernetes client integration.
- [ ] Ensure baseline test coverage exists for core state and client guards.

## Acceptance Checks

- [ ] App builds from clean clone with no errors.
- [ ] All abstractions have at least one implementation wired in DI.
- [ ] `ProfileRepository` round-trips project and namespace data correctly.
- [ ] Credential store saves and retrieves secrets without data loss.
- [ ] Baseline test projects execute successfully.

## Traceability Backlinks

- `docs/features/active/foundation-mvp/index.md`
- `docs/features/active/foundation-mvp/technical-plan-ui.md`
- `docs/features/active/foundation-mvp/test-plan.md`

