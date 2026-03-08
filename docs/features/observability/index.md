# Observability

## Purpose

Provide deep trace and log diagnostics with reusable query workflows, dashboarded metrics, and cross-navigation from other features.

## Scope

- Trace timeline and correlation workflows
- Metrics dashboard and tile management
- Saved query system per project environment
- OTLP provider support and normalization
- Cross-linking from Service Bus and AKS to observability views

## Logical Outcome

A provider-agnostic observability workspace that supports both quick triage and deep trace analysis.

## Dependencies

- Depends on `docs/features/foundation-mvp/`
- Integrates with `docs/features/service-bus/` and `docs/features/aks/`

## Source Traceability

- Canonical feature scope: `docs/features/observability/index.md`
- Supporting context: `docs/ARCHITECTURE.md`, `docs/DESIGN.md`

## Deliverables

- `docs/features/observability/technical-plan-backend.md`
- `docs/features/observability/technical-plan-ui.md`
- `docs/features/observability/test-plan.md`

## Migration Notes

Cross-link behaviors should be specified here as shared contracts with Service Bus and AKS features.
