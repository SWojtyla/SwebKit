# Status - service-bus-sbinspector-parity

---

title: "Status - service-bus-sbinspector-parity"
owner: "Unassigned"
state: "In Progress"
jira: "not linked"
branch: ""
started: "2026-03-28"
last_updated: "2026-03-28"

---

## Quick summary

Wave 1 through Wave 5 implementation is complete for parity-critical Service Bus operations in scope. Wave 5 finalizes message template parity with searchable template picking, explicit template selection, and inline validation feedback for invalid rename/edit inputs while preserving existing composer and Service Bus UX patterns.

**Jira:** not linked

**Current focus:** Final readiness review and sign-off preparation for completed parity waves.

## Progress checklist

### Wave 0 - Planning and parity baseline

- [x] Create active feature folder and core planning docs
- [x] Capture parity scope, assumptions, constraints, and wave sequence
- [x] Define test strategy and risk controls
- [ ] Confirm stakeholder acceptance criteria for each wave

### Wave 1 - Critical entity and message management

- [x] Add queue/topic/subscription enable/disable support (backend contract + Azure/demo implementation complete)
- [x] Add single-message delete from message list and DLQ contexts (active message-list delete wired; DLQ delete/resubmit already present)
- [x] Add purge-all workflow with production safety confirmations (active and DLQ purge wired through `ConfirmDialog`)
- [x] Add auto-refresh after mutative operations in this wave
- [x] Add or update backend/unit/component tests for critical operations

### Wave 2 - Advanced filtering and filtered operations

- [x] Add multi-field filters with explicit operators and logical composition
- [x] Add filter persistence and filter on/off toggle behavior
- [x] Add delete filtered messages flow with preview and confirmation
- [x] Add export filtered messages flow (JSON only for parity wave; CSV deferred follow-up)
- [x] Add tests for filtering logic, persistence, and filtered actions

### Wave 3 - Column customization and density

- [x] Add column chooser for built-in fields
- [x] Add custom-property columns for message application properties
- [x] Persist per-view column profiles and row density preferences
- [x] Keep keyboard navigation and accessibility consistent after customization
- [x] Add component tests for column profile/state persistence

### Wave 4 - Pagination and load-more

- [x] Add load-more paging behavior for large message sets
- [x] Preserve filter/sort/selection semantics across pages
- [x] Ensure paging interactions are responsive in Blazor Hybrid
- [x] Add tests for paging continuation and regression scenarios

### Wave 5 - Message templates

- [x] Add template create/save/update/delete flows in message composer
- [x] Add template apply flow for queue/topic send scenarios
- [x] Persist templates with clear environment or namespace scope rules
- [x] Add tests for template lifecycle and invalid-template handling

## Completed

- Active feature folder created with required planning documents.
- Severity-based parity gaps mapped to implementation waves.
- Architecture, pitfall, and documentation coupling constraints captured.
- Scope decisions captured: no theming/settings parity in this feature, and filtered export is JSON-first with CSV deferred.
- `IServiceBusClient` extended for Wave 1 backend operations (entity enable/disable, active completion, purge).
- `AzureServiceBusClient` updated with status-aware listing, entity state toggles, active completion, purge-all loops, and AZ-1/CS-2 guardrail alignment.
- `DemoServiceBusClient` updated with in-memory status toggles, active completion, and purge behavior.
- App test stubs updated to satisfy new interface methods; targeted Service Bus app tests pass.
- New core tests added for Wave 1 demo backend operations.
- `EntityTree` now shows Active/Disabled badges and invokes queue/topic/subscription enable-disable APIs with post-toggle tree reload.
- `MessageListView` now supports active single-message delete and mode-aware purge-all via `ConfirmDialog`, with production typing gate for purge and post-mutation list/count refresh.
- `DlqView` now forces list refresh after resubmit/delete single and batch operations via refresh token wiring.
- `EntityTreeTests` and `MessageListViewTests` now cover status/toggle calls and delete/purge confirmation + invocation behavior.
- `MessageListView` now supports Wave 2 advanced filtering:
  - global Filters on/off toggle and Advanced filter on/off toggle
  - per-rule add/remove/enable behaviors
  - rule fields for Application Property, Enqueued Time, Delivery Count, and Sequence Number
  - type-aware operators including contains/equals/not-equals/regex (text), numeric comparisons, and date comparisons
  - logical AND composition over enabled configured rules
- Saved filters now persist and restore:
  - text filter value
  - filters enabled/disabled state
  - advanced filter enabled/disabled state
  - advanced rule definitions
  - legacy text-only saved filters remain compatible
- `MessageListView` now supports Delete Filtered with confirmation preview and mode-aware backend routing:
  - active mode uses `CompleteMessagesAsync(entityPath, sequenceNumbers)`
  - DLQ mode uses `CompleteDeadLetterAsync(entityPath, sequenceNumbers)`
- Filtered export in `MessageListView` is now JSON-only for this wave; CSV/NDJSON options are removed from filtered export UX.
- `MessageListViewTests` now include Wave 2 coverage for:
  - advanced filtering across application-property + numeric + date criteria
  - saved filter persistence/apply behavior including legacy text-only filter compatibility
  - Delete Filtered routing in both active and DLQ modes
- Wave 2 test hardening added explicit saved-filter UI roundtrip coverage (configure/save/apply) and stricter backend routing assertions to ensure active mode never calls DLQ completion APIs (and vice versa), including post-mutation reload verification.
- `MessageListView` now supports Wave 3 column customization:
  - built-in column chooser toggles for message-list columns
  - add/remove custom `ApplicationProperties` columns rendered per message
  - explicit reset-to-default for columns and row density
- `UiStateRepository` now persists message-list preferences per `{namespaceId}:{entityPath}:{mode}` scope:
  - built-in column visibility profile
  - custom-property column list
  - row density
  - backward-compatible behavior for legacy `ui-state.json` without Wave 3 fields
- `MessageListViewTests` now include Wave 3 coverage for:
  - built-in column chooser toggling
  - custom-property column add/remove
  - preference restore behavior
  - reset-to-default behavior
  - keyboard row navigation after column customization
- `UiStateFilterTests` now include Wave 3 persistence coverage for roundtrip serialization and backward compatibility with missing/null preference fields.
- `MessageListView` now supports Wave 4 pagination/load-more behavior using an expanding message window:
  - explicit `Load More` control with window status indicator (`loaded/total` plus next target)
  - backward-compatible windowing by increasing peek request count instead of changing `IServiceBusClient` contracts
  - continuity-safe behavior for existing text/advanced filters and multi-select state as additional messages are loaded
  - refresh and mutation reloads continue using the active window size to avoid collapsing user context mid-session
- `MessageListViewTests` now include Wave 4 coverage for:
  - load-more expanding loaded window size when total available messages exceed current window
  - disabled load-more state when all available messages are already loaded
  - filter and selection continuity after load-more expansion
- `TemplatePicker` now supports Wave 5 template list usability completion:
  - searchable template list (name/subject/content-type)
  - explicit row selection highlight for template browsing
  - inline validation feedback for invalid rename (blank/duplicate) and duplicate edit-property keys
- `MessageComposer` now exposes stable template action selectors used by component tests without changing runtime UX behavior.
- `MessageComposerTests` now include Wave 5 coverage for:
  - create/save template persistence from composer (`Save as Template`)
  - apply template and send integration path, including callback invocation and message field propagation
- New `TemplatePickerTests` now cover Wave 5 picker behavior for:
  - search + apply selection
  - invalid rename validation
  - edit and delete lifecycle behavior
- App data persistence tests now use `SWEBKIT_APPDATA_ROOT` test override support in `AppDataPaths` plus serialized test collection scope for deterministic profile-data isolation.

## Remaining

- Final readiness review and feature close-out decision.
- Deferred follow-up tracking for CSV export and any theming/settings parity requests.

## Blockers

- No implementation blocker for Wave 5 scope.
- Full-suite baseline outside the required Wave 5 targeted test command remains unchanged and should be re-verified during final readiness review.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Wave 5 targeted validation complete; final readiness review pending
- Wave 5 validation:
  - `dotnet build SwebKit.slnx`: pass (warnings only, no new Wave 5 blockers)
  - `dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj --filter "FullyQualifiedName~MessageComposerTests|FullyQualifiedName~TemplatePickerTests"`: pass (12/12)
- Wave 4 validation:
  - `dotnet build SwebKit.slnx`: pass
  - `dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj --filter "FullyQualifiedName~MessageListViewTests"`: pass
- Wave 3 validation:
  - `dotnet build SwebKit.slnx`: pass
  - `dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj --filter "FullyQualifiedName~MessageListViewTests"`: pass
  - `dotnet test tests/SwebKit.Core.Tests/SwebKit.Core.Tests.csproj --filter "FullyQualifiedName~UiStateFilterTests"`: pass
- Full-suite note:
  - `dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj --no-build`: 4 unrelated failures
  - `dotnet test tests/SwebKit.Core.Tests/SwebKit.Core.Tests.csproj --no-build`: 1 unrelated failure

## Assumptions

- No Jira ticket link is available at planning time.
- High- and medium-severity parity items are mandatory for this feature's success criteria.
- Theming/settings parity is explicitly out of scope for this feature.
- Filtered export parity in this feature is JSON-first; CSV export is deferred follow-up work.

## Notes

- This feature must preserve SwebKit UX consistency and safety-first production behaviors while adding SBInspector-level capabilities.
- Any implementation that changes Service Bus behavior must update `docs/architecture/functionalities/service-bus.md` in the same change set.
