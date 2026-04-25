# Service Bus

## What Is Supported

The hosted Blazor Service Bus workspace remains the superset surface. Unless a bullet below explicitly calls out WinUI, advanced multi-rule filters, filtered delete/export, purge, row-density control, custom application-property columns, and full template-management workflows still refer to the hosted workspace while WinUI tracks the narrower native baseline called out here.

- Incident Timeline backend can surface best-effort queue/subscription symptoms for explicitly mapped Service Bus entities, combining peeks plus runtime properties and degrading coverage per entity instead of failing the whole source.
- Add/remove global Service Bus namespaces in the UI.
- Connect namespaces from stored credentials.
- Restore cached namespace connection snapshots immediately, then reconnect each connectable namespace independently in the background.
- The hosted Blazor Service Bus workspace remains the superset surface for advanced multi-rule filtering, filtered delete/export, purge, row-density control, and custom application-property columns.
- The native WinUI Service Bus workspace now supports namespace reconnect, entity browsing, active and DLQ peeks, single-message delete, load-more, text filtering, saved text filters, built-in field visibility preferences, template save/apply, send or schedule from the compose dialog, namespace-level scheduled-message history, and confirmation-gated DLQ or scheduled destructive actions.
- Browse queues, topics, and subscriptions with status surfaced as Active/Disabled.
- Entity rows surface message amount metrics plus pin/unpin only; operational actions are handled outside the row.
- Open Active, Open DLQ, and Enable/Disable actions from the selected-entity action bar (Active/DLQ are not applicable to topics).
- Peek active and dead-letter messages.
- Delete a single selected active message from `MessageListView`.
- The hosted Blazor workspace can build advanced multi-field filters (Application Property, Enqueued Time, Delivery Count, Sequence Number) with explicit operators and logical AND composition.
- The hosted Blazor workspace can toggle filters globally on/off and advanced rules on/off without losing configured criteria.
- The hosted Blazor workspace can save and restore message filters per scope, including text filter, advanced rules, and enabled-state toggles.
- The hosted Blazor workspace can delete filtered messages from active or DLQ mode with preview/confirmation and post-operation refresh.
- The hosted Blazor workspace can export filtered messages as JSON from `MessageListView`.
- The hosted Blazor workspace can purge all messages in the current mode (active or DLQ).
- Configure visible built-in message-list columns from a column chooser.
- The hosted Blazor workspace can add and remove custom message-list columns backed by `ApplicationProperties` keys.
- The hosted Blazor workspace persists row density and full column preferences per namespace/entity/mode scope, including reset-to-default; the native WinUI baseline currently persists built-in field visibility only.
- Expand loaded message windows with `Load More` in active and DLQ list modes for large result sets.
- The hosted Blazor workspace can compose, replay, edit, and schedule messages.
- The hosted Blazor workspace can manage message templates from composer workflows (create/save, search, apply, rename, duplicate, edit, delete).
- Cancel scheduled messages and view scheduled message history.
- Resubmit dead-letter messages to original or target entity, processing the full requested sequence set across receive batches.
- Complete dead-letter messages with the same exhaustive sequence matching.
- Use production-safe confirmation dialogs for destructive actions.
- Favorite Service Bus resources through the shared operator workspace model, with the Service Bus page, dashboard pins, command palette, and top-bar workspace hub all reading the same canonical favorite snapshots.
- The hosted Service Bus workspace fully saves and restores the active entity or scheduled tab, open tab set, and namespace-pane collapse state. The native WinUI workspace already participates in route-first restore after namespace reconnect, with broader reopen and navigation hardening still in progress.

## Core Runtime Flow

1. `ServiceBusPage` reads configured namespaces from `AppStateService` and asks `IServiceBusNamespaceBootstrapper` to build the initial page state from configuration, cached connection snapshots, and demo-mode state.
2. Each namespace that should reconnect resolves credentials and attempts connection through `IServiceBusNamespaceBootstrapper.ConnectAsync`, so the page only owns row state and per-namespace progress updates.
3. `EntityTree` loads queues/topics/subscriptions, surfaces entity status, and invokes enable/disable operations.
4. `MessageListView` applies text + advanced filtering rules in-memory over loaded messages, persists/reapplies saved filter profiles, applies per-scope column and row-density preferences, supports expanding windows via `Load More`, and calls `IServiceBusClient` operations for delete selected, delete filtered, JSON export, and purge.
5. `DlqView` continues to support existing DLQ resubmit/complete workflows and shares filtered-delete capability through `MessageListView` when in DLQ mode. `AzureServiceBusClient` routes DLQ complete and resubmit through `DeadLetterSequenceProcessor`, which keeps receiving until the requested sequence set is exhausted, releases non-target messages predictably, and fails explicitly if the broker is drained before all requested sequence numbers are found.
6. `MessageComposer` can save templates to profile-backed app state and apply templates selected from `TemplatePicker` before send/replay/schedule actions.
7. `TemplatePicker` supports in-dialog search and inline validation for invalid template rename/edit inputs, then persists template mutations through `AppStateService`.
8. Destructive mutations are gated by `ConfirmDialog`, and post-mutation refresh is handled via list reload plus refresh-token wiring for DLQ flows.
9. `ServiceBusPage` publishes semantic workspace snapshots for the active tab and tab set; shell-level recent/favorite reopen flows navigate first and then rehydrate the page state.

## Main Code Locations

- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
- `src/SwebKit.App/Services/ServiceBusNamespaceBootstrapper.cs`
- `src/SwebKit.App/Components/ServiceBus/EntityTree.razor`
- `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`
- `src/SwebKit.App/Components/ServiceBus/MessageComposer.razor`
- `src/SwebKit.App/Components/ServiceBus/DlqView.razor`
- `src/SwebKit.App/Components/ServiceBus/ScheduledMessages.razor`
- `src/SwebKit.App/Components/Shared/ConfirmDialog.razor`
- `src/SwebKit.WinUI/Views/ServiceBus/ServiceBusPage.xaml`
- `src/SwebKit.WinUI/Views/ServiceBus/ServiceBusPage.xaml.cs`
- `src/SwebKit.WinUI/ViewModels/ServiceBus/ServiceBusPageViewModel.cs`
- `src/SwebKit.Core/Abstractions/IServiceBusClient.cs`
- `src/SwebKit.Core/Abstractions/IServiceBusNamespaceBootstrapper.cs`
- `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`
- `src/SwebKit.Azure/ServiceBus/IncidentTimeline/ServiceBusEvidenceSignalSource.cs`
- `src/SwebKit.Azure/ServiceBus/DeadLetterSequenceProcessor.cs`
- `src/SwebKit.Core/Configuration/ScheduledMessageRepository.cs`
- `src/SwebKit.Core/Configuration/UiStateRepository.cs`

## Important Notes

- Incident timeline Service Bus evidence is mapping-first: only entities explicitly linked under `AppConfig.IncidentTimeline.WorkloadMappings` are queried for incident evidence.
- The current adapter is best-effort and optimized for queue/subscription mappings. Unsupported or inaccessible mapped entities surface degraded source coverage instead of causing a full incident-timeline failure.
- `AzureServiceBusClient` supports both connection-string and AAD-style setup paths.
- Scoped entity path connection strings are handled to surface only reachable entities.
- Entity status toggles are exposed through `SetQueueEnabledAsync`, `SetTopicEnabledAsync`, and `SetSubscriptionEnabledAsync`.
- Active single-message delete uses `CompleteMessagesAsync(entityPath, sequenceNumbers)` from `MessageListView`.
- Purge-all uses `PurgeMessagesAsync(entityPath, deadLetter)` for both active and DLQ modes.
- Existing DLQ resubmit/complete paths now share `DeadLetterSequenceProcessor` so selected sequence numbers are processed across batches instead of only the first receive window.
- If any requested sequence numbers are still missing after the dead-letter receiver is drained, the operation fails explicitly with the missing sequence numbers.
- Pagination/load-more is implemented as an expanding peek window (request count grows by the configured page size) so existing `IServiceBusClient` contracts remain unchanged.
- `MessageListView` surfaces window state (`loaded/total` and next target) and disables load-more when the loaded window reaches the known entity total.
- Refreshes (auto-refresh, mutation reloads, explicit refresh token updates) continue with the active window size to preserve filter and selection continuity.
- Template interactions are profile-backed through `ProfileRepository` and exposed via `AppStateService.MessageTemplates`.
- Template picker invalid-input safeguards include blank/duplicate name checks and duplicate property-key validation during template edits.
- Filtered delete routing is mode-aware:
  - active mode uses `CompleteMessagesAsync(entityPath, sequenceNumbers)`
  - DLQ mode uses `CompleteDeadLetterAsync(entityPath, sequenceNumbers)`
- Filtered export for parity Wave 2 is JSON-only; CSV export is intentionally deferred.
- Production protections rely on the current production-marked configuration and are enforced by `ConfirmDialog` at UI interaction level.
- The current WinUI parity baseline stops short of the hosted advanced rule builder, filtered delete/export, purge, row-density control, and custom application-property column workflows.
- Service Bus UI uses a collapsible entity panel and a responsive message detail drawer (push on wide screens, overlay on narrow).
- Entity names in the entity list wrap to full visibility (no single-line truncation/horizontal-scroll pattern).
- Topic rows retain expand/collapse behavior; queue/subscription operational actions are centralized in the selected-entity action bar.
- Favorite and unfavorite changes update immediately in the entity list, and the dashboard pinned panel plus shell workspace surfaces reflect the same canonical Service Bus resource list.
- `ServiceBusPage` keeps legacy `ServiceBusEntityLinks` synchronized for compatibility, but the canonical shell-level contract is now `FavoriteResources` plus page-owned semantic restore state.
- Message list row density and column profiles are persisted in `UiStateRepository` per `{namespaceId}:{entityPath}:{mode}` scope and can be reset to defaults from the column chooser.
- WinUI saved text filters are persisted in `UiStateRepository` per `{namespaceId}:{entityPath}` scope, while built-in field visibility preferences are persisted per `{namespaceId}:{entityPath}:{mode}` scope.
- Namespace pane collapsed/expanded state is now persisted in `UiStateRepository`, so it survives the same atomic app-data save and backup recovery path as the rest of the local UI state.
- Named favorites and recent-resource reopen flows restore route-first, then rebuild tabs from semantic tab-state payloads after the namespace reconnect fan-out completes.
- The underlying restore path exists for the native workspace, but broader reopen and navigation hardening is still part of the active WinUI parity follow-up.
- Demo namespaces and cached reconnect semantics are composed through `IServiceBusNamespaceBootstrapper`; `ServiceBusPage` preserves the visible namespace list and per-row progress while the background reconnect fan-out runs.
- Local scheduled-message metadata now persists through the same atomic write and `.bak` recovery path used by the other app-data repositories, so a partial write does not wipe the scheduled-message history list.
- Service Bus settings remain reachable from shell navigation and unconfigured-state CTAs; the main route header no longer reserves space for a one-off Settings button.

## Validation Pointers

- `tests/SwebKit.App.Tests/ServiceBusPageTests.cs`
- `tests/SwebKit.App.Tests/MessageComposerTests.cs`
- `tests/SwebKit.App.Tests/TemplatePickerTests.cs`
- `tests/SwebKit.WinUI.Tests/ServiceBusPageViewModelTests.cs`
- `tests/SwebKit.Core.Tests/ServiceBusNamespaceTests.cs`
- `tests/SwebKit.Core.Tests/ScheduledMessageRepositoryTests.cs`
- `tests/SwebKit.Azure.Tests/ServiceBus/DeadLetterSequenceProcessorTests.cs`
