# Service Bus

## What Is Supported

- Configure multiple namespaces in Settings using connection strings or Entra-based authentication.
- Test namespace connectivity without returning raw SDK/credential details to the browser.
- Browse queues, topics, and subscriptions with message counts. Subscription queries run only when a topic is expanded; topic rows carry collapsed DLQ rollups.
- Restore the last selected namespace/entity and keep namespace, entity, active/DLQ mode, and message selection in the URL for deep links and browser history.
- Show a namespace overview before an entity is selected.
- Peek active and dead-letter messages with an expanding message window and `Load more` support.
- Search and filter loaded messages by text, application property, enqueued time, delivery count, sequence number, and pinned session ID.
- Save scoped filter profiles, select visible built-in/application-property columns, and persist row density/auto-refresh preferences.
- Inspect body, properties, headers, and DLQ details in a resizable message detail pane.
- Compose, replay, edit, schedule, and batch-send messages.
- Manage reusable message templates: create, apply, rename, duplicate, edit, and delete.
- View and cancel locally tracked scheduled messages.
- Complete active or dead-letter messages, manually dead-letter active messages, purge a mode, resubmit DLQ messages, and resend selected active/DLQ messages.
- Batch replay messages from pasted JSON with preview and confirmation.
- Open the entity command palette and the contextual agent for the selected Service Bus scope.
- Run entirely against realistic demo clients when demo mode is active.

## Resend Semantics

Resend is move-like, not copy-only:

1. Receive each requested sequence under PeekLock from the active entity or its DLQ.
2. Resolve the destination from `NServiceBus.FailedQ`, falling back to the viewed entity; MSMQ-era `@machine` suffixes are stripped.
3. Clone the message with a newly generated message ID.
4. Send the clone to the resolved target.
5. Complete the original only after the send succeeds.

`MessageSequenceProcessor` continues receiving across broker batches until the requested sequence set is exhausted. Missing sequences produce an explicit failure rather than a partial-success response. Manual move-to-DLQ uses broker settlement (`DeadLetterMessageAsync`) rather than copy-and-delete.

## Runtime Flow

```text
/service-bus
  → ServiceBusPage
      → useProfile → configured namespaces
      → URL state: ns / entity / entityName / view / msg / seq
      → EntityTree
          → useSbQueues / useSbTopics
          → expanded TopicRow → useSbSubscriptions
      → selected entity
          → useSbEntityStats
          → useSbPeekMessages or useSbPeekDlq
          → local expanding messageWindow
          → MessageList + MessageDetail
      → compose/batch/scheduled/template overlays
      → mutation hook
          → sidecar /api/servicebus/{nsId}/...
          → IServiceBusConnectionPool
          → IServiceBusClient (Azure or demo)
          → targeted React Query invalidation
```

`ServiceBusPage` owns page navigation and overlays. `MessageList` owns loaded-list filtering, selection, bulk progress, column preferences, and auto-refresh. React Query owns remote topology, counts, peek windows, and mutation state.

## Query and Endpoint Behavior

- Topology (`queues`, `topics`, `subscriptions`) uses a five-minute stale window; deployments change topology less frequently than message counts.
- Entity stats use a shorter stale window and seed from already-loaded topology counts where possible.
- Peek failures retry once instead of holding the page in a minutes-long loading state across repeated Azure SDK timeouts.
- Entity paths are encoded as one route segment; this is required for subscription paths such as `topic/subscriptions/name`.
- `invalidateServiceBusQueries` centralizes the exact topology/message/stats/scheduled query keys after mutations.
- The sidecar resolves namespace IDs against `ProfileRepository`/demo state and obtains pooled clients from `IServiceBusConnectionPool`.
- Connection-test failures return a sanitized diagnostic; raw Azure SDK exception messages are logged server-side only.

## Persistence

- Namespace configuration and templates are profile-backed through `ProfileRepository`.
- Last namespace/entity selection and message-list preferences are browser-local stores under `web/src/lib/stores/`.
- Drill-down state is URL-backed so reload, back/forward, and copied links retain the current workspace.
- Scheduled-message history is persisted by `ScheduledMessageRepository`; it is metadata for messages scheduled through SwebKit, not an exhaustive broker-side schedule index.

## Main Code Locations

- `web/src/components/service-bus/ServiceBusPage.tsx` — page state, URL restoration, overlays, refresh
- `web/src/components/service-bus/EntityTree.tsx` — queue/topic/subscription browser and counts
- `web/src/components/service-bus/NamespaceOverview.tsx` — namespace summary
- `web/src/components/service-bus/MessageList.tsx` — filters, columns, selection, bulk operations
- `web/src/components/service-bus/MessageDetail.tsx` — message inspection/actions
- `web/src/components/service-bus/MessageComposer.tsx` — send/replay/edit/schedule workspace
- `web/src/components/service-bus/BatchSendPanel.tsx` / `BatchReplayPanel.tsx`
- `web/src/components/service-bus/TemplateManager.tsx` / `TemplatePicker.tsx`
- `web/src/components/service-bus/ScheduledMessages.tsx`
- `web/src/components/service-bus/bulkOps.ts` / `resendHelpers.ts` / `filterLogic.ts`
- `web/src/lib/hooks/useServiceBus.ts` — queries, mutations, invalidation
- `web/src/lib/stores/sb-selection.ts` / `sb-preferences.ts`
- `src-sidecar/Endpoints/ServiceBusEndpoints.cs` — HTTP contract
- `src-sidecar/Services/SidecarServiceBusConnectionPool.cs` — pooled client lifetime
- `src/SwebKit.Core/Abstractions/IServiceBusClient.cs`
- `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`
- `src/SwebKit.Azure/ServiceBus/MessageSequenceProcessor.cs`
- `src/SwebKit.Core/Configuration/ScheduledMessageRepository.cs`

## Important Constraints

- Subscriptions are receive-only; send-like UI actions normalize the target to the parent topic.
- Do not persist demo namespace IDs as real configuration. `ConfigEndpoints.SaveProfileAsync` strips demo overlays before saving.
- JSON-bound application-property values arrive as `JsonElement`; the Azure client must normalize them to AMQP-supported primitives.
- Preserve selection/filter continuity when refreshing or expanding the message window.
- Every destructive or move-like action requires an explicit confirmation in production-facing UI.
- Never expose connection strings or raw SDK exception text through endpoint responses.

## Validation Pointers

- `web/e2e/service-bus.spec.ts` — complete UI workflow coverage
- `web/e2e/service-bus-url-state.spec.ts` — deep links and history
- `web/src/components/service-bus/filterLogic.test.ts`
- `web/src/components/service-bus/bulkOps.test.ts`
- `web/src/components/service-bus/resendHelpers.test.ts`
- `web/src/lib/hooks/useServiceBus.test.ts`
- `tests/SwebKit.Sidecar.Tests/ServiceBusEndpointsMutationTests.cs`
- `tests/SwebKit.Sidecar.Tests/SidecarServiceBusConnectionPoolTests.cs`
- `tests/SwebKit.Azure.Tests/ServiceBus/MessageSequenceProcessorTests.cs`
- `tests/SwebKit.Azure.Tests/ServiceBus/ResendTests.cs`
- `tests/SwebKit.Core.Tests/ServiceBusNamespaceTests.cs`
