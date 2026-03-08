# Technical Plan — Foundation MVP: Backend

## Status

- Current: In Progress

## Architecture

```
SwebKit.Core
  Domain/          — Project, ProjectEnvironment, ServiceBusNamespace, SbEntityLink
  Abstractions/    — IServiceBusClient, IObservabilityProvider, IAksClient, ICredentialStore, IAppEventBus
  Configuration/   — ProfileRepository, UiStateRepository, AppDataPaths
  Services/        — AppStateService, AppEventBus

SwebKit.Azure
  ServiceBus/      — AzureServiceBusClient
  Observability/   — AppInsightsObservabilityProvider

SwebKit.Kubernetes
  AksClient/       — KubernetesAksClient

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
  - Files: `SwebKit.slnx`, `src/*/*.csproj`
- [ ] Confirm domain types align with design contract.
  - Files: `src/SwebKit.Core/Domain/*`
- [ ] Confirm interface signatures are stable for downstream features.
  - Files: `src/SwebKit.Core/Abstractions/*`
- [ ] Validate persistence models for profiles and UI state.
  - Files: `src/SwebKit.Core/Configuration/*`
- [ ] Validate credential storage abstraction and Windows implementation.
  - Files: `src/SwebKit.Core/Abstractions/ICredentialStore.cs`, `src/SwebKit.App/Platforms/Windows/WindowsCredentialStore.cs`
- [ ] Complete and verify Service Bus client integration.
  - Files: `src/SwebKit.Azure/ServiceBus/*`
- [ ] Complete and verify App Insights provider integration.
  - Files: `src/SwebKit.Azure/Observability/*`
- [ ] Complete and verify Kubernetes client integration.
  - Files: `src/SwebKit.Kubernetes/AksClient/*`
- [ ] Ensure baseline test coverage exists for core state and client guards.
  - Files: `tests/SwebKit.Core.Tests/*`, `tests/SwebKit.Azure.Tests/*`, `tests/SwebKit.Kubernetes.Tests/*`

## Acceptance Checks

- [ ] App builds from clean clone with no errors.
- [ ] All abstractions have at least one implementation wired in DI.
- [ ] `ProfileRepository` round-trips project and namespace data correctly.
- [ ] Credential store saves and retrieves secrets without data loss.
- [ ] Baseline test projects execute successfully.

## Traceability Backlinks

- `docs/features/foundation-mvp/index.md`
- `docs/features/foundation-mvp/technical-plan-ui.md`
- `docs/features/foundation-mvp/test-plan.md`
