# Status - Service Bus Enhancements

---

title: "Status - Service Bus Enhancements"
owner: ""
state: "Implemented"
branch: "main"
started: "2026-03-08"
last_updated: "2026-03-08"

---

## Quick summary

All planned features implemented.

## Completed

- **IServiceBusClient**: `ScheduleMessageAsync` + `CancelScheduledMessageAsync` added.
- **AzureServiceBusClient**: Both new methods implemented via Azure SDK sender APIs.
- **FakeServiceBusClient**: Stubs added for new interface methods.
- **ScheduledMessageRepository** (new): JSON store at `scheduled-messages.json`; AddAsync, RemoveAsync, GetByNamespace, GetByEntity.
- **UiStateRepository**: Extended with `SavedFilters` + GetFilters/SaveFilterAsync/DeleteFilterAsync helpers.
- **AppDataPaths**: `ScheduledMessagesJson` path added.
- **MauiProgram**: `ScheduledMessageRepository` registered as singleton.
- **`_Imports.razor`**: `@using SwebKit.Core.Configuration` added.
- **ServiceBusModels.cs**: `RemapRules` + `ScheduledMessageEntry` models added.
- **MessageDetailPane.razor**: `OnEdit`, `OnReplay`, `OnSchedule` EventCallbacks; buttons shown conditionally.
- **MessageComposer.razor**: Modes: Compose / Edit / Replay / Schedule; `PrefillMessage` prefill; replay target picker + remap rules; schedule datetime picker; records to `ScheduledMessageRepository`.
- **MessageListView.razor**: `NamespaceId` param; saved filters (save/load/delete); export (JSON / NDJSON / CSV via `SwebKit.downloadText`).
- **ScheduledMessages.razor** (new): Lists scheduled entries; Cancel calls broker + removes local entry; Remove removes local only.
- **ServiceBusPage.razor**: Modal `MessageComposer` overlay for Edit/Replay/Schedule; `IsScheduled` tab type; 🕐 button per namespace; `NamespaceId` wired to `MessageListView`.
- **keyboardShortcuts.js**: `Ctrl+E` (edit), `Ctrl+R` (replay), `Ctrl+Shift+S` (schedule), `Ctrl+Shift+P` (peek); `SwebKit.downloadText` blob helper.

## Remaining / Follow-up

- Wire `SbEditResubmit` / `SbReplay` / `SbSchedule` / `SbPeek` shortcut names from `OnShortcut` in the main layout to invoke the active tab's actions.
