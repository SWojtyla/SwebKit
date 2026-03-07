# Test Plan - AKS

## Status

- Current: Planned

## Scope

- Validate AKS diagnostic workflows for workloads, pods, events, logs, and terminal operations.
- Validate port-forward and live tail lifecycle handling under expected failure and reconnect paths.
- Validate observability cross-links from AKS context into related diagnostic views.
- Preserve traceability with active feature technical plan deliverables.

## Test Levels

- Unit tests (`tests/SwebKit.Kubernetes.Tests/`, `tests/SwebKit.Core.Tests/`): client behavior, model mapping, and guardrails.
- Component tests (`tests/SwebKit.App.Tests/`): AKS page rendering, watch controls, and action safety behavior.
- Integration tests (cluster-mocked): listing, logs, and port-forward orchestration contracts.
- Smoke tests (manual): end-to-end namespace and workload diagnostics in non-production clusters.

## Key Scenarios

- [ ] AKS-001: Namespace and workload list load with accurate status indicators.
- [ ] AKS-002: Pod log tail starts, reconnects after transient failures, and stops cleanly.
- [ ] AKS-003: Multi-pod tail and pod watch update views without duplicate events.
- [ ] AKS-004: Port-forward lifecycle create, show status, and terminate behavior is reliable.
- [ ] AKS-005: Embedded terminal launches with selected cluster context and namespace.
- [ ] AKS-006: AKS to observability deep-link preserves correlation context where available.

## Command Placeholders

- `dotnet test tests/SwebKit.Kubernetes.Tests/SwebKit.Kubernetes.Tests.csproj -p:Configuration=Debug`
- `dotnet test tests/SwebKit.Core.Tests/SwebKit.Core.Tests.csproj -p:Configuration=Debug`
- `dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj -p:Configuration=Debug`
- `dotnet test SwebKit.slnx`

## Traceability Backlinks

- `docs/features/aks/index.md`
- `docs/features/aks/technical-plan.md`
- `docs/plans/docs-rework-traceability/index.md`
