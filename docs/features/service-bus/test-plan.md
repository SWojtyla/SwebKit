# Test Plan - Service Bus

## Status

- Current: Partially implemented (namespace model, repository, client guard, and SB-UI-BUG-01 validation)

## Scope

- Validate global namespace model and CRUD via `ProfileRepository`.
- Validate `SbEntityLink` model and `ProjectEnvironment` entity links.
- Validate `AzureServiceBusClient` connection-string constructor guard.
- Validate operational Service Bus workflows for safe message inspection and mutation.
- Validate DLQ remediation, message composition, templates, and repeatable scenario execution.
- Validate production safety gates for mutative actions and confirmation behavior.
- Validate focused UI bug fixes for DLQ rendering accuracy, table responsiveness, horizontal scroll usability, and entity label decoding.

## Test Levels

- Unit tests (`tests/SwebKit.Core.Tests/`): namespace model, entity links, profile repository CRUD.
- Unit tests (`tests/SwebKit.Azure.Tests/`): client construction guards, mapping, operation guards.
- Component tests (`tests/SwebKit.App.Tests/`): DLQ views, composer, template UX, filters, confirmations.
- Integration tests (service-mocked): send, peek, resubmit, and complete pipelines.
- Smoke tests (manual): end-to-end queue/topic workflows in a non-production environment.

## Key Scenarios

- [x] SB-NS-001: `ProfileRepository` stores and retrieves global namespaces across add/remove.
- [x] SB-NS-002: `ProjectEnvironment.ServiceBusEntityLinks` defaults to empty; add/remove works.
- [x] SB-NS-003: `AzureServiceBusClient` connection-string ctor parses valid strings without throwing.
- [x] SB-NS-004: `AzureServiceBusClient` connection-string ctor throws on invalid strings.
- [ ] SB-001: DLQ batch resubmit and complete support multi-select with visible progress.
- [ ] SB-002: Composer sends payloads with custom properties to selected entity.
- [ ] SB-003: Template save, load, update, and delete lifecycle works across sessions.
- [ ] SB-004: Scenario execution runs ordered steps and supports cancellation.
- [ ] SB-005: Favorites and live counters refresh without breaking filter state.
- [ ] SB-006: Production environment mutative actions always require explicit confirmation.
- [x] SB-UI-101: DLQ list shows explicit `showing X of Y` semantics when render window is smaller than total count.
- [x] SB-UI-102: DLQ mode is visually distinct from normal message list mode.
- [ ] SB-UI-103: Manual smoke test with large DLQ (>1000) validates clarity and navigation.
- [x] SB-UI-104: Entity tree exposes explicit Active/DLQ actions and renders both counts simultaneously.
- [x] SB-UI-201: Responsive message-table hooks validated in component tests (scroll owner + per-column class strategy).
- [ ] SB-UI-301: Horizontal scroll interaction remains usable when left settings panel is visible (manual interaction check pending).
- [ ] SB-UI-302: Horizontal scroll interaction remains usable after collapsing the left panel (manual interaction check pending).
- [x] SB-UI-303: Service Bus page exposes a left-pane collapse/expand control and applies collapsed layout class in component tests.
- [x] SB-UI-401: Entity labels render without encoded artifacts like `&#...;`.

## Command Placeholders

- `dotnet test tests/SwebKit.Azure.Tests/SwebKit.Azure.Tests.csproj -p:Configuration=Debug`
- `dotnet test tests/SwebKit.Core.Tests/SwebKit.Core.Tests.csproj -p:Configuration=Debug`
- `dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj -p:Configuration=Debug`
- `dotnet test SwebKit.slnx`

## Traceability Backlinks

- `docs/features/service-bus/index.md`
- `docs/features/service-bus/technical-plan-ui-bugfixes.md`
- `docs/features/service-bus/technical-plan.md`
- `docs/plans/docs-rework-traceability/index.md`
