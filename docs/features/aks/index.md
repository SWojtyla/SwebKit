# AKS

## Purpose

Provide operational depth for Kubernetes workloads with reliable live diagnostics, managed tunnels, and in-context terminal workflows.

## Scope

- Live and resilient pod log tailing
- Multi-pod tailing and health indicators
- Port-forward lifecycle management
- Embedded terminal integration
- Real-time pod watch and events timeline
- Cross-linking to observability

## Logical Outcome

An AKS troubleshooting workspace that supports day-to-day cluster diagnostics without leaving the app.

## Dependencies

- Depends on `docs/features/foundation-mvp/`
- Integrates with `docs/features/observability/`

## Source Traceability

- Canonical feature scope: `docs/features/aks/index.md`
- Supporting context: `docs/ARCHITECTURE.md`, `docs/DESIGN.md`

## Deliverables

- `docs/features/aks/technical-plan-backend.md`
- `docs/features/aks/technical-plan-ui.md`
- `docs/features/aks/test-plan.md`

## Migration Notes

AKS-to-observability navigation behavior should remain synchronized with the Observability feature plan.
