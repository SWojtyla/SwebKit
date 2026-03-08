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
- E2E (Playwright): browser infrastructure smoke tests

## Key Scenarios

- Save/Load filter persists per-entity and restores UI state.
- Message pretty-render correctly for JSON/HTML/text/base64.
- Edit message and resubmit => message appears in target entity (mocked send)
- Schedule message returns sequence number and is listed in ScheduledMessages.
- Cancel scheduled message removes metadata and cancels in Service Bus (mocked).
- Replay with remap applies property mappings.

## Implemented Tests

### `tests/SwebKit.Core.Tests/` (45 tests total)

| File | Tests | Covers |
|------|-------|--------|
| `RemapRulesTests.cs` | 6 | `RemapRules.IsEmpty` for all combinations |
| `ScheduledMessageRepositoryTests.cs` | 7 | Add, Remove, GetByNamespace, GetByEntity (case-insensitive), persistence roundtrip, new-instance-empty |
| `UiStateFilterTests.cs` | 8 | SaveFilter, GetFilter, DeleteFilter, overwrite-same-name, case-insensitive overwrite, scope isolation, persistence roundtrip |
| `TestHelpers.cs` | — | Shared `AppDataSandbox` helper (redirects APPDATA to temp dir) |

> **Note:** `AppDataSandbox` redirects the `APPDATA` env var. On Windows, `Environment.GetFolderPath(SpecialFolder.ApplicationData)` reads from the Windows shell APIs which do respect `%APPDATA%`. Tests that verify "no file exists" should NOT use the sandbox for isolation — use `NewInstance_StartsWithEmptyAll` pattern instead.

### `tests/SwebKit.App.Tests/` (49 tests total)

| File | Tests | Covers |
|------|-------|--------|
| `MessageComposerTests.cs` | 7 | Compose/Edit/Replay/Schedule mode rendering, prefill from SbMessage, re-prefill on reference change, Resubmit label |
| `ScheduledMessagesComponentTests.cs` | 5 | Empty state, Pending status for future entries, Enqueued status for past entries, entity path filter, Remove button |
| `MessageListViewTests.cs` (updated) | — | Added `ScheduleMessageAsync`/`CancelScheduledMessageAsync` stubs to `FakeServiceBusClient`; registered `UiStateRepository` in constructor |
| `EntityTreeTests.cs` (updated) | — | Added `ScheduleMessageAsync`/`CancelScheduledMessageAsync` stubs to `FakeServiceBusClient` |
| `ServiceBusPageTests.cs` (updated) | — | Added `ScheduledMessageRepository` and `UiStateRepository` DI registrations |
| `SwebKit.App.Tests.csproj` (updated) | — | Added `MessageComposer.razor`, `TemplatePicker.razor`, `ScheduledMessages.razor` as `<RazorComponent>` |
| `_Imports.razor` (updated) | — | Added `@using SwebKit.Core.Configuration` |

### `tests/SwebKit.E2E.Tests/` (3 tests — new project)

| File | Tests | Covers |
|------|-------|--------|
| `PlaywrightSmokeTests.cs` | 3 | Playwright browser launch, JS evaluation, inline HTML rendering and element selection |

Playwright browsers must be installed before the first run:
```
powershell -ExecutionPolicy Bypass -File tests/SwebKit.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```
The post-build target in the `.csproj` does this automatically on each build.

## Commands

Run focused tests during development:

```
dotnet test tests/SwebKit.Core.Tests/SwebKit.Core.Tests.csproj -p:Configuration=Debug
dotnet test tests/SwebKit.Azure.Tests/SwebKit.Azure.Tests.csproj -p:Configuration=Debug
dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj -p:Configuration=Debug
dotnet test tests/SwebKit.E2E.Tests/SwebKit.E2E.Tests.csproj -p:Configuration=Debug
```

Run all at once (sequential — each project as a separate command):
```
dotnet test tests/SwebKit.Core.Tests/SwebKit.Core.Tests.csproj -p:Configuration=Debug --no-restore && dotnet test tests/SwebKit.App.Tests/SwebKit.App.Tests.csproj -p:Configuration=Debug --no-restore && dotnet test tests/SwebKit.E2E.Tests/SwebKit.E2E.Tests.csproj -p:Configuration=Debug --no-restore
```
