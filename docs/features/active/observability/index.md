# Observability

---

title: "Observability"
owner: ""
status: "Planned"
created: ""
updated: "2026-03-08"

---

## Goal

Provide deep trace and log diagnostics with reusable query workflows, dashboarded metrics, and cross-navigation from other features.

## Value

Enable quick triage and deep trace analysis across services with provider-agnostic tooling and saved queries.

## Scope

- Trace timeline and correlation workflows
- Metrics dashboard and tile management
- Saved query system per project environment
- OTLP provider support and normalization
- Cross-linking from Service Bus and AKS to observability views

## Logical outcome

A provider-agnostic observability workspace supporting triage and deep analysis.

## Dependencies

- Depends on `docs/features/archive/foundation-mvp/`
- Integrates with `docs/features/archive/service-bus/` and `docs/features/archive/aks/`

## Source traceability

- Canonical feature scope: `docs/features/active/observability/index.md`
- Supporting context: `docs/architecture/architecture.md`, `docs/architecture/design.md`

## Deliverables

- `docs/features/active/observability/backend.md`
- `docs/features/active/observability/frontend.md`
- `docs/features/active/observability/test-plan.md`

## Migration notes

Cross-link behaviors should be specified here as shared contracts with Service Bus and AKS features.

## Quick links

- Status: `status.md`
- Backend plan: `backend.md`
- Frontend plan: `frontend.md`
- Tests: `test-plan.md`
- Decisions: `decisions.md`
