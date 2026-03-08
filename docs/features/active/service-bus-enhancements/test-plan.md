# Test Plan - Service Bus Enhancements

## Scope

- Quick wins: message rendering, saved filters, export, keyboard shortcuts
- Edit & Resubmit: prefill composer, modify body/properties, send to same or other entity
- Scheduled Message Manager: schedule/cancel and list scheduled items
- Replay-to-Other-Namespace: replay with property remap

## Test Levels

- Unit tests: `IServiceBusClient` new APIs, `ScheduledMessageRepository`
- Component tests: composer prefills, edit/resubmit flow, ScheduledMessages UI
- Integration (mocked): schedule -> cancel, replay -> target deliver

## Key Scenarios

- Save/Load filter persists per-entity and restores UI state.
- Message pretty-render correctly for JSON/HTML/text/base64.
- Edit message and resubmit => message appears in target entity (mocked send)
- Schedule message returns sequence number and is listed in ScheduledMessages.
- Cancel scheduled message removes metadata and cancels in Service Bus (mocked).
- Replay with remap applies property mappings.

## Commands

Run focused tests during development:

```
dotnet test tests/SwebKit.Core.Tests/SwebKit.Core.Tests.csproj -p:Configuration=Debug
dotnet test tests/SwebKit.Azure.Tests/SwebKit.Azure.Tests.csproj -p:Configuration=Debug
dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj -p:Configuration=Debug
```
