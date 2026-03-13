# Service Bus

## What Is Supported

- Add/remove global Service Bus namespaces in the UI.
- Connect namespaces from stored credentials.
- Browse queues, topics, and subscriptions.
- Open entity tabs for active queue/topic and DLQ views.
- Peek active and dead-letter messages.
- Compose, replay, edit, and schedule messages.
- Cancel scheduled messages and view scheduled message history.
- Resubmit dead-letter messages to original or target entity.
- Complete dead-letter messages.
- Pin entity links per environment from settings.

## Core Runtime Flow

1. Service Bus page loads namespace definitions from `AppStateService`.
2. Each namespace resolves credentials via `ICredentialStore` and attempts connection.
3. Page components call `IServiceBusClient` operations for list/peek/send/DLQ workflows.
4. Entity tabs preserve selected context (namespace, entity path, mode).

## Main Code Locations

- `src/SwebKit.App/Components/Pages/ServiceBusPage.razor`
- `src/SwebKit.App/Components/ServiceBus/EntityTree.razor`
- `src/SwebKit.App/Components/ServiceBus/MessageListView.razor`
- `src/SwebKit.App/Components/ServiceBus/MessageComposer.razor`
- `src/SwebKit.App/Components/ServiceBus/DlqView.razor`
- `src/SwebKit.App/Components/ServiceBus/ScheduledMessages.razor`
- `src/SwebKit.Core/Abstractions/IServiceBusClient.cs`
- `src/SwebKit.Azure/ServiceBus/AzureServiceBusClient.cs`
- `src/SwebKit.Core/Configuration/ScheduledMessageRepository.cs`

## Important Notes

- `AzureServiceBusClient` supports both connection-string and AAD-style setup paths.
- Scoped entity path connection strings are handled to surface only reachable entities.
- DLQ resubmit path uses peek-lock receive, forward send, and explicit complete.
- Production protections rely on current environment and are enforced at UI interaction level.

## Validation Pointers

- `tests/SwebKit.App.Tests/ServiceBusPageTests.cs`
- `tests/SwebKit.Core.Tests/ServiceBusNamespaceTests.cs`
- `tests/SwebKit.Core.Tests/ScheduledMessageRepositoryTests.cs`
