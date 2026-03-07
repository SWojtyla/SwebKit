# Foundation and MVP

## Purpose

Deliver a stable baseline application shell, core domain model, and real service connectivity so all later features build on a working foundation.

## Scope

- Solution scaffolding and dependency wiring
- Project and environment domain model
- Core abstractions and app state services
- Initial implementations for Service Bus, Observability, and AKS clients
- Baseline app shell and page skeletons

## Logical Outcome

A runnable app where project and environment context drives all major pages, with initial end-to-end paths available for each pillar.

## Dependencies

- None (first feature in sequence)

## Source Traceability

- Canonical feature scope: `docs/features/foundation-mvp/index.md`
- Supporting context: `docs/ARCHITECTURE.md`, `docs/DESIGN.md`

## Deliverables

- `docs/features/foundation-mvp/technical-plan.md`
- `docs/features/foundation-mvp/test-plan.md`

## Migration Notes

Use this feature folder as the active implementation and testing source for Foundation and MVP.
