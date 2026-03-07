# Technical Plan - AKS

## Status

- Current: Pending

## Implementation Sequence

1. Expand pod log viewer for robust live tailing.
2. Add multi-pod tailing and rendering constraints.
3. Implement port-forward session management and persistence.
4. Add embedded terminal via JS interop bridge.
5. Add real-time pod watch and reconnection logic.
6. Add events timeline and filter controls.
7. Finalize AKS to observability cross-link behavior.

## Detailed Tasks

- [ ] Add buffered streaming log pipeline and throttled rendering.
  - Files: `src/SwebKit.App/Components/Aks/PodLogView.razor`
- [ ] Add pod restart detection and reconnect UX.
  - Files: `src/SwebKit.App/Components/Aks/PodLogView.razor`, `src/SwebKit.Kubernetes/AksClient/*`
- [ ] Add multi-pod combined tail view with per-pod prefixes.
  - Files: `src/SwebKit.App/Components/Aks/*`
- [ ] Add port-forward service abstraction and lifecycle panel.
  - Files: `src/SwebKit.App/Services/PortForwardService.cs`, `src/SwebKit.App/Components/Aks/PortForwardPanel.razor`
- [ ] Persist and restore active tunnels across restarts.
  - Files: `src/SwebKit.Core/Configuration/UiStateRepository.cs`, `src/SwebKit.App/Services/PortForwardService.cs`
- [ ] Add terminal JS interop layer and Blazor host component.
  - Files: `src/SwebKit.App/wwwroot/js/terminalInterop.js`, `src/SwebKit.App/Components/Aks/TerminalView.razor`
- [ ] Add Kubernetes watch adapter with reconnect policy.
  - Files: `src/SwebKit.Kubernetes/AksClient/*`, `src/SwebKit.App/Components/Aks/WorkloadOverview.razor`
- [ ] Add events timeline view and filters.
  - Files: `src/SwebKit.App/Components/Aks/EventsTimeline.razor`
- [ ] Add AKS to observability navigation contract.
  - Files: `src/SwebKit.App/Components/Aks/*`, `src/SwebKit.App/Components/Pages/ObservabilityPage.razor`

## Acceptance Checks

- [ ] Live tail remains stable through pod restarts.
- [ ] Multi-pod tailing is usable and bounded by UI limits.
- [ ] Port-forwards can be started, viewed, and stopped reliably.
- [ ] Embedded terminal supports interactive shell commands.
- [ ] Watch mode updates pod table without full refresh.
- [ ] Events timeline reflects recent namespace events.

## Traceability Backlinks

- `docs/features/aks/index.md`
- `docs/features/aks/test-plan.md`
- `docs/plans/docs-rework-traceability/index.md`
