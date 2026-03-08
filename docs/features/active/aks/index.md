# AKS

---

title: "AKS"
owner: ""
status: "Planned"
created: ""
updated: ""

---

## Goal

Provide operational depth for Kubernetes workloads with reliable live diagnostics, managed tunnels, and in-context terminal workflows.

## Value

Enable cluster troubleshooting and diagnostics inside the app to reduce context-switching and speed investigations.

## Scope

- Live and resilient pod log tailing
- Multi-pod tailing and health indicators
- Port-forward lifecycle management
- Embedded terminal integration
- Real-time pod watch and events timeline
- Cross-linking to observability

## Logical outcome

An AKS troubleshooting workspace supporting day-to-day cluster diagnostics without leaving the app.

## Dependencies

- Depends on `docs/features/active/foundation-mvp/`
- Integrates with `docs/features/observability/`

## Source traceability

- Canonical feature scope: `docs/features/active/aks/index.md`
- Supporting context: `docs/ARCHITECTURE.md`, `docs/DESIGN.md`

## Deliverables

- `docs/features/active/aks/technical-plan-backend.md`
- `docs/features/active/aks/technical-plan-ui.md`
- `docs/features/active/aks/test-plan.md`

## Migration notes

AKS-to-observability navigation behavior should remain synchronized with the Observability feature plan.
