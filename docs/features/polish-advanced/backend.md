<!-- Copied from technical-plan-backend.md -->

# Backend

## Status

- Current: Pending

## Implementation Sequence

1. Add recent command ranking persistence to `UiStateRepository`.
2. Add import/export for project configuration in `ProfileRepository`.
3. Validate platform-specific credential and process behavior.
4. Run performance profiling on core service hot paths.

## Detailed Tasks

- [ ] Add recent command ranking persistence.
- [ ] Add import and export project configuration workflows.
- [ ] Validate platform-specific credential and process behavior.
- [ ] Profile and optimize `AppStateService` and hot client paths.

## Acceptance Checks

- [ ] Config export produces a file that re-imports cleanly (secrets excluded).
- [ ] Credential store behaves correctly on all supported platforms.
- [ ] No observable latency regression on core state-change operations.
