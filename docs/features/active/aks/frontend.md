<!-- Copied from technical-plan-ui.md -->

# Frontend

## Status

- Current: Pending

## Component Hierarchy

```
AksPage (Pages/)
├── WorkloadOverview (Aks/)
├── PodLogView (Aks/)        — buffered streaming, multi-pod
├── PortForwardPanel (Aks/)  — active tunnels list
├── TerminalView (Aks/)      — xterm.js via JS interop
└── EventsTimeline (Aks/)
```

## Blazor Patterns & Pitfalls

See [`docs/pitfalls/blazor-maui.md`](../../../pitfalls/blazor-maui.md) for the full reference. Entries most relevant here: **BL-2** (`InvokeAsync`), **BL-6** (JS interop after DOM), **BL-7** (`IAsyncEnumerable` cancellation), **BL-8** (throttle `StateHasChanged`).

## Implementation Sequence

1. Add buffered streaming log pipeline and throttled render to `PodLogView`.
2. Add pod restart detection UX and reconnect indicator.
3. Add multi-pod combined tail view with per-pod color prefixes.
4. Build `PortForwardPanel` showing active tunnels with start/stop controls.
5. Build `TerminalView` JS interop bridge and xterm.js host.
6. Add pod watch binding to `WorkloadOverview` for live updates.
7. Add `EventsTimeline` with filter controls.
8. Implement AKS → Observability cross-link navigation.

## Detailed Tasks

- [ ] Add buffered streaming log pipeline and throttled rendering.
  - Files: `src/SwebKit.App/Components/Aks/PodLogView.razor`
- [ ] Add pod restart detection and reconnect UX.
  - Files: `src/SwebKit.App/Components/Aks/PodLogView.razor`
- [ ] Add multi-pod combined tail view with per-pod prefixes.
  - Files: `src/SwebKit.App/Components/Aks/PodLogView.razor`
- [ ] Add port-forward lifecycle panel.
  - Files: `src/SwebKit.App/Components/Aks/PortForwardPanel.razor`
- [ ] Add terminal JS interop layer and Blazor host component.
  - Files: `src/SwebKit.App/wwwroot/js/terminalInterop.js`, `src/SwebKit.App/Components/Aks/TerminalView.razor`
- [ ] Add Kubernetes watch binding to `WorkloadOverview`.
  - Files: `src/SwebKit.App/Components/Aks/WorkloadOverview.razor`
- [ ] Add events timeline view and filters.
  - Files: `src/SwebKit.App/Components/Aks/EventsTimeline.razor`
- [ ] Add AKS to observability navigation contract.
  - Files: `src/SwebKit.App/Components/Pages/AksPage.razor`

## Acceptance Checks

- [ ] Live tail remains stable through pod restarts.
- [ ] Multi-pod tailing is usable and bounded by render limits.
- [ ] Port-forwards can be started, viewed, and stopped reliably.
- [ ] Embedded terminal supports interactive shell commands.
- [ ] Watch mode updates pod table without full refresh.
- [ ] Events timeline reflects recent namespace events.
