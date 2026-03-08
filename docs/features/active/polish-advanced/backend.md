# Backend Plan - Polish and Advanced

---

title: "Backend Plan - Polish and Advanced"
owner: ""
status: "Not started"

---

<!-- Copied from technical-plan-backend.md -->

## Status

- Current: Pending

## Implementation Sequence

1. Add recent command ranking persistence to `UiStateRepository`.
2. Add import/export for project configuration in `ProfileRepository`.
3. Validate platform-specific credential and process behavior.
4. Run performance profiling on core service hot paths.

## Detailed Tasks

- [ ] Add recent command ranking persistence.
  - Files: `src/SwebKit.Core/Configuration/UiStateRepository.cs`
- [ ] Add import and export project configuration workflows.
  - Files: `src/SwebKit.Core/Configuration/ProfileRepository.cs`
- [ ] Validate platform-specific credential and process behavior.
  - Files: `src/SwebKit.App/Platforms/*`, `docs/PLATFORM-NOTES.md`
- [ ] Profile and optimize `AppStateService` and hot client paths.
  - Files: `src/SwebKit.Core/Services/*`, `src/SwebKit.Azure/*`

## Acceptance Checks

- [ ] Config export produces a file that re-imports cleanly (secrets excluded).
- [ ] Credential store behaves correctly on all supported platforms.
- [ ] No observable latency regression on core state-change operations.

## Traceability Backlinks

- `docs/features/active/polish-advanced/index.md`
- `docs/features/active/polish-advanced/technical-plan-ui.md`
- `docs/features/active/polish-advanced/test-plan.md`
