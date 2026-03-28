# Backend Plan - service-bus-sbinspector-parity

---

title: "Backend Plan - service-bus-sbinspector-parity"
owner: "Unassigned"
status: "Planned"

---

## Goal

Extend Service Bus backend contracts and Azure implementation so SwebKit can support SBInspector-level entity and message management features, advanced filtering, filtered actions, pagination, and template persistence while preserving reliability, cancellation safety, and clear permission handling, with scope focused on functional parity and operational capability.

## Impacted areas

- Core contracts and models:
  - `src/SwebKit.Core/Abstractions/IServiceBusClient.cs`
  - `src/SwebKit.Core/Domain/AppConfig.cs`
  - `src/SwebKit.Core/Configuration/ProfileRepository.cs`
- Azure implementation:
  - `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`
- Test projects:
  - `tests/SwebKit.Azure.Tests/`
  - `tests/SwebKit.Core.Tests/`
  - `tests/SwebKit.App.Tests/` (for contract-backed component behavior)

## Design

### Wave 1 - Critical entity/message management contracts

- Add backend operations for entity status changes (enable/disable for queues/topics/subscriptions).
- Add backend operations for single-message delete and purge-all with explicit result summaries.
- Keep destructive operations result-oriented (counts, failures, partial success details) to support safe UI feedback.

### Wave 2 - Filtering and filtered actions

- Introduce a filter contract model that supports:
  - Multiple fields
  - Explicit operators
  - Logical composition
- Add backend operations for delete filtered and JSON export filtered so UI can avoid re-implementing message matching logic.

### Wave 3 - Column and density persistence dependencies

- Provide persistence schema support for view preferences needed by frontend:
  - Column profiles (built-in + custom property columns)
  - Row density preferences
- Ensure preference models are optional and backward compatible in config files.

### Wave 4 - Pagination/load-more

- Add paged list contract support with continuation semantics suitable for large message sets.
- Ensure filtering and paging can be composed consistently (filter definition + continuation context).

### Wave 5 - Templates

- Add model/persistence support for message templates (create/update/delete/apply metadata).
- Keep template schema environment-aware where needed, without introducing breaking config changes.

### Azure SDK and .NET guardrails (must apply across waves)

- Align connection validation and listing semantics to avoid claim mismatch surprises (AZ-1).
- Preserve scoped connection string behavior clarity (AZ-2).
- Dispose async pageable enumerators correctly (AZ-3).
- Do not rely on `required` for runtime safety; validate at boundaries (CS-1).
- Preserve cancellation by explicitly rethrowing `OperationCanceledException` (CS-2).

## API / Contracts

Planned contract additions and model updates (names indicative, to be finalized during implementation):

- Entity admin operations:
  - Enable or disable queue/topic/subscription
  - Return structured operation results (success, reason, affected entity)
- Message operations:
  - Delete single message by stable identifier context
  - Purge all messages for selected active/DLQ scope
- Filter model:
  - Field/operator/value condition list
  - Logical composition metadata
- Filtered action operations:
  - Delete filtered message set
  - Export filtered message set (JSON in this feature scope; CSV deferred)
- Paging model:
  - Page size + continuation token context
  - Stable ordering assumptions documented
- Template persistence model:
  - Template identity, scope, payload, metadata

Backward compatibility notes:

- Existing methods should remain available during transition where possible.
- New persistence fields must deserialize safely for existing user profiles with defaults.

## Tasks

### Wave 1 - Critical entity/message management [dotnet-expert] (sequential)

- [ ] Define and approve entity status and destructive operation contracts
- [ ] Implement `IServiceBusClient` contract updates
- [ ] Implement Azure admin/message operations in `AzureServiceBusClient`
- [ ] Add safety-oriented operation result models
- [ ] Add unit tests in `tests/SwebKit.Azure.Tests/` and `tests/SwebKit.Core.Tests/`

### Wave 2 - Advanced filtering and filtered actions [dotnet-expert] (depends on Wave 1)

- [ ] Add multi-field filter contract and matching behavior
- [ ] Implement delete filtered and JSON export filtered backend operations
- [ ] Validate operator behavior consistency across active/DLQ sources
- [ ] Add targeted tests for filter operator matrix and filtered actions

### Wave 3 - Preference persistence support [dotnet-expert] (parallel with frontend wave start)

- [ ] Add config model extensions for column profiles and row density
- [ ] Update profile repository serialization/deserialization defaults
- [ ] Add regression tests for config backward compatibility

### Wave 4 - Pagination contract support [dotnet-expert] (depends on Wave 2)

- [ ] Add page result model and continuation semantics
- [ ] Implement paging in message retrieval operations
- [ ] Add tests for continuation behavior and edge cases

### Wave 5 - Template persistence support [dotnet-expert] (parallel with frontend template UX)

- [ ] Add template models and persistence rules
- [ ] Implement template CRUD support in core services/repositories
- [ ] Add tests for template lifecycle and malformed data handling

### Documentation and decision hygiene [manual] (ongoing)

- [ ] Update `decisions.md` when non-obvious tradeoffs are made
- [ ] Ensure Service Bus behavior updates also update `docs/architecture/functionalities/service-bus.md`

## Migration and runtime changes

- Migration steps:
  - Extend `AppConfig` with optional fields for filters, columns, density, and templates.
  - Keep defaults non-breaking for existing profiles.
  - Provide safe fallback when new fields are absent.
- Runtime behavior changes:
  - Destructive operations expose richer result reporting.
  - Listing/retrieval APIs may adopt paging semantics for large datasets.
- Rollback strategy:
  - Preserve compatibility by ignoring unknown optional fields and keeping old defaults where feasible.

## Validation

- Unit tests: Not started
- Integration tests: Not started
- Manual checks:
  - Permission and claim handling for admin operations
  - Scoped connection string behavior
  - Cancellation propagation during long-running operations

## Notes

- Backend work should be completed wave-by-wave with contract stability checks before frontend wiring.
- CSV export and settings/theming parity are intentionally deferred beyond this feature scope.
- If architecture-altering choices are required, record them in `decisions.md` and keep architecture docs aligned.
