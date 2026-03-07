# Technical Plan - Service Bus

## Status

- Current: Pending

## Implementation Sequence

1. Add DLQ multi-select and batch operation framework.
2. Implement message composer with payload and property editors.
3. Add template persistence and management UX.
4. Add scenario orchestration with task-queue progress.
5. Add favorites, live counters, and auto-refresh.
6. Add filter-state persistence and advanced SQL mode.
7. Add export and clipboard workflows.
8. Validate production safety behavior across mutative actions.

## Detailed Tasks

- [ ] Implement DLQ batch selection and action bar.
  - Files: `src/SwebKit.App/Components/ServiceBus/DlqView.razor`
- [ ] Implement batch progress reporting in task queue.
  - Files: `src/SwebKit.Core/Abstractions/ITaskQueue.cs`, `src/SwebKit.Core/Services/TaskQueueService.cs`
- [ ] Build message composer UI and entity target selector.
  - Files: `src/SwebKit.App/Components/ServiceBus/MessageComposer.razor`
- [ ] Add message template model and persistence.
  - Files: `src/SwebKit.Core/Domain/*`, `src/SwebKit.Core/Configuration/ProfileRepository.cs`
- [ ] Build template picker and management actions.
  - Files: `src/SwebKit.App/Components/ServiceBus/TemplatePicker.razor`
- [ ] Add scenario model and scenario runner orchestration.
  - Files: `src/SwebKit.Core/Domain/*`, `src/SwebKit.App/Components/ServiceBus/ScenarioEditor.razor`
- [ ] Add favorite entities section with live count refresh.
  - Files: `src/SwebKit.App/Components/ServiceBus/EntityTree.razor`
- [ ] Add auto-refresh controls and visibility-aware pause.
  - Files: `src/SwebKit.App/Components/ServiceBus/*`, `src/SwebKit.App/wwwroot/js/*`
- [ ] Persist and restore filter state by entity path.
  - Files: `src/SwebKit.Core/Configuration/UiStateRepository.cs`, `src/SwebKit.App/Components/ServiceBus/*`
- [ ] Add export and copy actions for rows and bodies.
  - Files: `src/SwebKit.App/Components/ServiceBus/*`
- [ ] Enforce production confirm dialogs for mutative actions.
  - Files: `src/SwebKit.App/Components/Shared/ConfirmDialog.razor`, `src/SwebKit.App/Components/ServiceBus/*`

## Acceptance Checks

- [ ] Batch DLQ resubmit and complete work with progress.
- [ ] Composer sends messages with custom properties.
- [ ] Templates save, load, and execute correctly.
- [ ] Scenarios run sequentially and are cancellable.
- [ ] Favorites show active and DLQ counts.
- [ ] Auto-refresh and filter persistence behave as expected.
- [ ] Production safety gates are consistently enforced.

## Traceability Backlinks

- `docs/features/service-bus/index.md`
- `docs/features/service-bus/test-plan.md`
- `docs/plans/docs-rework-traceability/index.md`
