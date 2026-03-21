# Backend Refactor

## Goal

Improve the backend codebase quality to be maintainable, understandable, and testable. No new features — only quality improvements to existing code.

## Scope

- Test coverage: add unit tests for all Azure, Kubernetes, Redis, DevOps client implementations
- Critical bugs: fix static process registry, thread-safety issues
- Error handling: add logging to swallowed exceptions
- Configuration: validate configs early, extract shared JSON options
- Code duplication: shared helpers for try-patterns, JSON options
- DI: fix clients that require manual Configure() calls

## Non-goals

- New integrations or Azure services
- Options pattern migration across entire codebase (targeted fixes only)
- Integration/E2E test infrastructure changes

## Dependencies

None — self-contained refactor.

## Risks

- Changing exception handling may surface previously hidden errors in demo mode
- Static process registry fix must not break running port-forward sessions
- Adding validation to config constructors may break existing serialization/deserialization

## Quick links

- [Status](status.md)
- [Backend plan](backend.md)
- [Test plan](test-plan.md)
- [Decisions](decisions.md)
