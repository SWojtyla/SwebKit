# Archive Summary - Service Bus Enhancements

---

title: "Archive Summary - Service Bus Enhancements"
owner: ""
completed_date: "2026-03-08"
pr: ""
commit: ""

---

## Goal

Improve Service Bus workspace productivity with message rendering, saved filters, export, keyboard shortcuts, edit/resubmit, scheduled message management, and replay-to-other-namespace with remapping rules.

## Delivered

- **Edit & Resubmit**: `MessageComposer.razor` with Compose/Edit/Replay/Schedule modes; prefill from existing message; modal overlay in `ServiceBusPage`.
- **Scheduled Message Manager**: `ScheduleMessageAsync` / `CancelScheduledMessageAsync` on `IServiceBusClient`; `ScheduledMessageRepository` for local metadata persistence; `ScheduledMessages.razor` list with Cancel/Remove.
- **Replay-to-Other-Namespace**: Replay target picker with namespace selector and `RemapRules` (property remapping).
- **Saved Filters**: `UiStateRepository` extended with per-entity saved filter support; save/load/delete in `MessageListView`.
- **Message Export**: JSON / NDJSON / CSV export via `SwebKit.downloadText` blob helper.
- **Keyboard Shortcuts**: `Ctrl+E` (edit), `Ctrl+R` (replay), `Ctrl+Shift+S` (schedule), `Ctrl+Shift+P` (peek) wired via `ServiceBusShortcutEvent` through `IAppEventBus`.
- **MessageDetailPane**: Conditional Edit/Replay/Schedule action buttons.

## Key decisions

- Local scheduled message metadata storage — Azure has no list-scheduled API, so we store sequenceNumber/namespace/entity/time locally. Acceptable for dev tooling.
- Simple remap rules first — property remapping + body passthrough. Complex JSONPath/transforms deferred.
- Production guard — all mutative flows require explicit confirmation in production environments.

## Validation performed

- 45 unit tests (Core): `RemapRulesTests`, `ScheduledMessageRepositoryTests`, `UiStateFilterTests`
- 49 component tests (App): `MessageComposerTests`, `ScheduledMessagesComponentTests`, updated `MessageListViewTests`, `EntityTreeTests`, `ServiceBusPageTests`
- 3 E2E smoke tests (Playwright infrastructure)
- All 97 tests pass on build.

## Lessons learned

- `AppDataSandbox` helper is useful for test isolation but `Environment.GetFolderPath` on Windows reads shell APIs; use `NewInstance_StartsWithEmptyAll` pattern for "no file exists" tests.
- Event bus pattern (`ServiceBusShortcutEvent`) works well for decoupling keyboard shortcuts from page-specific logic.

## Follow-up

- Complex body transformation engine (JSONPath/transforms) — deferred to future work.

## Archive metadata

- Source: `docs/features/active/service-bus-enhancements/`
- Related: `docs/features/archive/service-bus/` (foundational Service Bus feature)
