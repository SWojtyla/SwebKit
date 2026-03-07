# Technical Plan - Foundation and MVP

## Status

- Current: In Progress

## Implementation Sequence

1. Validate solution scaffold and project references.
2. Finalize core domain and abstraction contracts in `SwebKit.Core`.
3. Finalize profile, UI-state, and credential storage behaviors.
4. Complete service client implementations for Azure and AKS pillars.
5. Wire DI and app-state propagation in `SwebKit.App`.
6. Complete page-level integration with real client calls.
7. Add baseline tests for core services and client guards.
8. Verify acceptance criteria and document gaps.

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
- [ ] Finalize MAUI Blazor shell composition.
  - Files: `src/SwebKit.App/MainPage.xaml`, `src/SwebKit.App/MauiProgram.cs`, `src/SwebKit.App/Components/*`
- [ ] Complete page interaction wiring and environment switching behavior.
  - Files: `src/SwebKit.App/Components/Pages/*`
- [ ] Ensure baseline test coverage exists for core state and client guards.
  - Files: `tests/SwebKit.Core.Tests/*`, `tests/SwebKit.Azure.Tests/*`, `tests/SwebKit.Kubernetes.Tests/*`

## Acceptance Checks

- [ ] App launches and loads baseline navigation.
- [ ] Project and environment switching updates all pages.
- [ ] Service Bus page can list entities and peek data.
- [ ] Observability page can execute baseline log query.
- [ ] AKS page can list workloads and pod info.
- [ ] Baseline test projects execute successfully.

## Traceability Backlinks

- `docs/features/foundation-mvp/index.md`
- `docs/features/foundation-mvp/test-plan.md`
- `docs/plans/docs-rework-traceability/index.md`
