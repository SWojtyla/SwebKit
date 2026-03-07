# Test Plan - Foundation and MVP

## Status

- Current: Planned

## Scope

- Validate baseline app shell, navigation, and environment switching behaviors.
- Validate core domain and state services that downstream features depend on.
- Validate initial Azure and AKS client guardrails and baseline connectivity paths.
- Use this file as the active feature-level test planning source.

## Test Levels

- Unit tests (`tests/SwebKit.Core.Tests/`): domain models, app state, event bus, command registry.
- Unit tests (`tests/SwebKit.Azure.Tests/`, `tests/SwebKit.Kubernetes.Tests/`): client validation and mapping guard paths.
- Component tests (`tests/SwebKit.App.Tests/`): app shell, top-level pages, and shared dialogs.
- Integration and smoke tests (manual or scripted): project setup flow and baseline page-level service calls.

## Key Scenarios

- [ ] FND-001: App boots into shell with expected navigation and default state.
- [ ] FND-002: Project and environment switching updates active context across pages.
- [ ] FND-003: Service Bus page baseline entity load path is reachable from configured environment.
- [ ] FND-004: Observability page baseline query path is reachable from configured environment.
- [ ] FND-005: AKS page baseline workload list path is reachable from configured environment.
- [ ] FND-006: Core services behave deterministically for add, update, delete, and select flows.

## Command Placeholders

- `dotnet test tests/SwebKit.Core.Tests/SwebKit.Core.Tests.csproj -p:Configuration=Debug`
- `dotnet test tests/SwebKit.Azure.Tests/SwebKit.Azure.Tests.csproj -p:Configuration=Debug`
- `dotnet test tests/SwebKit.Kubernetes.Tests/SwebKit.Kubernetes.Tests.csproj -p:Configuration=Debug`
- `dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj -p:Configuration=Debug`

## Traceability Backlinks

- `docs/features/foundation-mvp/index.md`
- `docs/features/foundation-mvp/technical-plan.md`
- `docs/plans/docs-rework-traceability/index.md`
