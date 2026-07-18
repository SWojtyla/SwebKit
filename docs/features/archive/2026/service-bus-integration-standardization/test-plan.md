# Service Bus Integration Standardization - Test Plan

## Unit Tests

### Infrastructure

**TC1 - `AzureServiceBusClient` delegates to underlying services**
- Given `IServiceBusAdminOperations`, `IServiceBusMessageOperations`, `IServiceBusStatsOperations` mocks
- When `ListQueuesAsync`, `PeekMessagesAsync`, `GetEntityStatsAsync` are called
- Then the corresponding service method is invoked with correct parameters

**TC2 - `ServiceBusClientConnectionFactory` parses connection strings safely**
- Given valid SAS connection strings, invalid strings, and Entra namespace strings
- When `CreateProperties` is called
- Then diagnostics contain only non-secret identifiers (no shared access key value)

**TC3 - `ServiceBusExceptionClassifier` identifies authentication failures**
- Given `RequestFailedException` with status 401/403, `UnauthorizedAccessException`, and generic exceptions
- When `IsAuthenticationFailure` is called
- Then returns true for auth failures and false otherwise

**TC4 - `DeadLetterSequenceProcessor` handles empty and partial matches**
- Given requested sequence numbers, some present in received messages, some missing
- When `ProcessAsync` is called
- Then matched messages are processed, unmatched released, missing throw after exhaustion

**TC5 - `ServiceBusWarmupCache` disposes replaced clients**
- Given two clients assigned to the same namespace id
- When a new client is stored and cache is invalidated
- Then previous clients are disposed exactly once

## Integration Tests

### UI Components

**TC6 - `ServiceBusPage` renders without `InvokeAsync(StateHasChanged)` flooding**
- Given namespace state changes
- When multiple updates fire rapidly
- Then `RequestRender()` coalesces updates to a single render

**TC7 - `ServiceBusGrid` filter and sort update UI**
- Given a list of queues and topics
- When filter text and sort column change
- Then displayed list updates correctly

**TC8 - `ServiceBusGrid` cancellation on dispose**
- Given active stats loading
- When component is disposed
- Then pending stats operations are cancelled without exceptions

### End-to-End

**TC9 - Namespace connection**
- Given a Service Bus namespace configuration
- When `ConnectAsync` is called
- Then client is created and `TestConnectionAsync` succeeds

**TC10 - Message peek and complete**
- Given a queue with messages
- When peeking and then completing by sequence number
- Then messages are removed from the queue

**TC11 - Dead-letter resubmit**
- Given a queue with dead-lettered messages
- When resubmitting to the active queue
- Then messages reappear in active queue and are removed from DLQ

## Manual Validation

**MV1 - UI responsiveness**
- Open Service Bus page, connect to namespace
- Filter entities rapidly, verify no UI lag

**MV2 - Memory profile**
- Open/close Service Bus page multiple times
- Verify no `IServiceBusClient` or `CancellationTokenSource` leaks via memory profiler

**MV3 - Error handling**
- Configure an invalid namespace
- Verify user-friendly error message without secret exposure

**MV4 - Demo mode**
- Switch to demo mode
- Verify `ServiceBusPage` loads demo namespaces and entities correctly

## Regression Tests

**RT1 - Existing Service Bus workflows still work**
- All peek/send/complete/schedule/dead-letter operations work as before

**RT2 - Build warnings**
- `dotnet build SwebKit.App.csproj` produces zero warnings

**RT3 - No interface breaking changes**
- `IServiceBusClient` consumers compile and run without changes
