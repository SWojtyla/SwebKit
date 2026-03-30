# Service Bus

## What Is Supported

- Add/remove global Service Bus namespaces in the UI.
- Connect namespaces from stored credentials.
- Browse queues, topics, and subscriptions with status surfaced as Active/Disabled.
- Entity rows surface message amount metrics plus pin/unpin only; operational actions are handled outside the row.
- Open Active, Open DLQ, and Enable/Disable actions from the selected-entity action bar (Active/DLQ are not applicable to topics).
- Peek active and dead-letter messages.
- Delete a single selected active message from `MessageListView`.
- Build advanced multi-field filters (Application Property, Enqueued Time, Delivery Count, Sequence Number) with explicit operators and logical AND composition.
- Toggle filters globally on/off and advanced rules on/off without losing configured criteria.
- Save and restore message filters per scope, including text filter, advanced rules, and enabled-state toggles.
- Delete filtered messages from active or DLQ mode with preview/confirmation and post-operation refresh.
- Export filtered messages as JSON from `MessageListView`.
- Purge all messages in the current mode (active or DLQ).
- Configure visible built-in message-list columns from a column chooser.
- Add and remove custom message-list columns backed by `ApplicationProperties` keys.
- Persist message-list row density and column preferences per namespace/entity/mode scope, including reset-to-default.
- Expand loaded message windows with `Load More` in active and DLQ list modes for large result sets.
- Compose, replay, edit, and schedule messages.
- Manage message templates from composer workflows (create/save, search, apply, rename, duplicate, edit, delete).
- Cancel scheduled messages and view scheduled message history.
- Resubmit dead-letter messages to original or target entity.
- Complete dead-letter messages.
- Use production-safe confirmation dialogs for destructive actions.
- Pin entity links per environment from settings.

## Core Runtime Flow

1. Service Bus page loads namespace definitions from `AppStateService`.
2. Each namespace resolves credentials via `ICredentialStore` and attempts connection.
3. `EntityTree` loads queues/topics/subscriptions, surfaces entity status, and invokes enable/disable operations.
4. `MessageListView` applies text + advanced filtering rules in-memory over loaded messages, persists/reapplies saved filter profiles, applies per-scope column and row-density preferences, supports expanding windows via `Load More`, and calls `IServiceBusClient` operations for delete selected, delete filtered, JSON export, and purge.
5. `DlqView` continues to support existing DLQ resubmit/complete workflows and shares filtered-delete capability through `MessageListView` when in DLQ mode.
6. `MessageComposer` can save templates to profile-backed app state and apply templates selected from `TemplatePicker` before send/replay/schedule actions.
7. `TemplatePicker` supports in-dialog search and inline validation for invalid template rename/edit inputs, then persists template mutations through `AppStateService`.
8. Destructive mutations are gated by `ConfirmDialog`, and post-mutation refresh is handled via list reload plus refresh-token wiring for DLQ flows.

## Main Code Locations

- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
- `src/SwebKit.App/Components/ServiceBus/EntityTree.razor`
- `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`
- `src/SwebKit.App/Components/ServiceBus/MessageComposer.razor`
- `src/SwebKit.App/Components/ServiceBus/DlqView.razor`
- `src/SwebKit.App/Components/ServiceBus/ScheduledMessages.razor`
- `src/SwebKit.App/Components/Shared/ConfirmDialog.razor`
- `src/SwebKit.Core/Abstractions/IServiceBusClient.cs`
- `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`
- `src/SwebKit.Core/Configuration/ScheduledMessageRepository.cs`

## Important Notes

- `AzureServiceBusClient` supports both connection-string and AAD-style setup paths.
- Scoped entity path connection strings are handled to surface only reachable entities.
- Entity status toggles are exposed through `SetQueueEnabledAsync`, `SetTopicEnabledAsync`, and `SetSubscriptionEnabledAsync`.
- Active single-message delete uses `CompleteMessagesAsync(entityPath, sequenceNumbers)` from `MessageListView`.
- Purge-all uses `PurgeMessagesAsync(entityPath, deadLetter)` for both active and DLQ modes.
- Existing DLQ resubmit/complete paths remain in place and trigger list refresh via DLQ refresh-token updates.
- Pagination/load-more is implemented as an expanding peek window (request count grows by the configured page size) so existing `IServiceBusClient` contracts remain unchanged.
- `MessageListView` surfaces window state (`loaded/total` and next target) and disables load-more when the loaded window reaches the known entity total.
- Refreshes (auto-refresh, mutation reloads, explicit refresh token updates) continue with the active window size to preserve filter and selection continuity.
- Template interactions are profile-backed through `ProfileRepository` and exposed via `AppStateService.MessageTemplates`.
- Template picker invalid-input safeguards include blank/duplicate name checks and duplicate property-key validation during template edits.
- Filtered delete routing is mode-aware:
  - active mode uses `CompleteMessagesAsync(entityPath, sequenceNumbers)`
  - DLQ mode uses `CompleteDeadLetterAsync(entityPath, sequenceNumbers)`
- Filtered export for parity Wave 2 is JSON-only; CSV export is intentionally deferred.
- Production protections rely on current environment and are enforced by `ConfirmDialog` at UI interaction level.
- Service Bus UI uses a collapsible entity panel and a responsive message detail drawer (push on wide screens, overlay on narrow).
- Entity names in the entity list wrap to full visibility (no single-line truncation/horizontal-scroll pattern).
- Topic rows retain expand/collapse behavior; queue/subscription operational actions are centralized in the selected-entity action bar.
- Message list row density and column profiles are persisted in `UiStateRepository` per `{namespaceId}:{entityPath}:{mode}` scope and can be reset to defaults from the column chooser.
- Namespace pane collapsed/expanded state remains persisted in local storage.

## Validation Pointers

- `tests/SwebKit.App.Tests/ServiceBusPageTests.cs`
- `tests/SwebKit.App.Tests/MessageComposerTests.cs`
- `tests/SwebKit.App.Tests/TemplatePickerTests.cs`
- `tests/SwebKit.Core.Tests/ServiceBusNamespaceTests.cs`
- `tests/SwebKit.Core.Tests/ScheduledMessageRepositoryTests.cs`
