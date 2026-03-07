# Phase 2 — Service Bus Power Features

**Status:** ⏳ Pending (starts after Phase 1 acceptance criteria met)
**Goal:** Turn the basic Service Bus viewer into a full debugging and operations tool — batch DLQ
operations, message composition, templates, scenarios, and smart presets.

---

## 1. DLQ Batch Operations

- [ ] Multi-select in DLQ list: checkbox column in `FluentDataGrid`, select all / select range
- [ ] Batch action bar appears when 2+ messages selected:
  - `[Resubmit Selected (N)]` `[Complete Selected (N)]` `[Move Selected (N) to...]`
- [ ] Batch resubmit: loop `ResubmitDeadLetterAsync` with per-message progress update in `ITaskQueue`
- [ ] Confirmation dialog for batch ops in any env (not just Prod); for Prod + >10 messages: require typing "CONFIRM"
- [ ] Progress: task entry in status bar showing "Resubmitting 47 messages... (12/47)"

---

## 2. Message Composer (Send / Replay)

**Component:** `Components/ServiceBus/MessageComposer.razor`

- [ ] Opens as a full-page tab or modal: "Send message to: [entity]"
- [ ] Body editor: Monaco Editor (`KqlEditor.razor` reused / renamed to `CodeEditor.razor`)
  - Language auto-detect: JSON / XML based on ContentType
  - Format-on-save button
- [ ] Property fields:
  - ContentType, CorrelationId, Subject, SessionId, MessageId (auto-generate toggle)
  - Custom properties: key-value pairs with type selector (String / Int / Bool / Double)
- [ ] Actions: `[Send]` `[Send & Close]` `[Save as Template...]`
- [ ] "Send to" selector (defaults to entity that opened the composer, can be changed)
- [ ] Prod confirmation: confirm before send if `Tier == Production`

---

## 3. Message Template System

**Storage:** `profiles.json` → `ProjectEnvironment.MessageTemplates: List<MessageTemplate>`

```
MessageTemplate
  Id: Guid
  Name: string
  EntityPath: string?       // default target entity, nullable
  ContentType: string?
  Body: string              // raw JSON/XML body
  ApplicationProperties: Dictionary<string, object>
  Subject: string?
  CorrelationId: string?
  CreatedAt: DateTimeOffset
```

- [ ] "Save as Template" dialog: enter name, optionally clear sensitive props
- [ ] Template picker: `Components/ServiceBus/TemplatePicker.razor`
  - Grid of saved templates with name, entity, created date
  - Click → loads into composer
  - Right-click → Rename, Delete, Duplicate
- [ ] Templates appear in Command Palette: "Send template: [name]"
- [ ] Template manager in Settings → Service Bus → Templates

---

## 4. Scenario System

**Storage:** `profiles.json` → `Project.Scenarios: List<Scenario>` (project-level, not env-level)

```
Scenario
  Id: Guid
  Name: string
  Description: string?
  Steps: List<ScenarioStep>

ScenarioStep
  Order: int
  EntityPath: string
  TemplateName: string
  DelayAfterMs: int         // delay before next step
  TargetEnvironment: string? // null = use current env
```

**Component:** `Components/ServiceBus/ScenarioEditor.razor`

- [ ] List/create/edit scenarios
- [ ] Step editor: drag-to-reorder, set entity, pick template, set delay
- [ ] "Run Scenario" button:
  - Sends steps sequentially with configured delays
  - Progress shown in `ITaskQueue` panel
  - Each step: mark sent / mark failed (with error inline)
  - Cancellable mid-run

---

## 5. Favorite Entities & Live Counts

- [ ] Right-click entity in `EntityTree.razor` → "Add to Favorites"
- [ ] Favorites section in left nav below main nav items
- [ ] Each favorite shows: name + active count + DLQ count (refreshed every 30s via `ITaskQueue` background timer)
- [ ] DLQ count shown in red badge when > 0
- [ ] Click favorite → opens entity view directly

---

## 6. Auto-Refresh

- [ ] Toolbar: `[Auto-refresh: Off ▾]` → Off / 10s / 30s / 60s dropdown
- [ ] Timer in component `@code` block using `PeriodicTimer` or `CancellationTokenSource`
- [ ] Last refreshed indicator: "Refreshed 5s ago" — updates every second
- [ ] Auto-refresh paused when tab is not active (detect via JS visibility API)

---

## 7. Filter State Persistence

- [ ] Each entity's last-used filter state persisted in `ui-state.json` under `LastUsedFilters[entityPath]`
- [ ] `FilterState` includes: time range, level selection, text search, correlation ID, custom property filters
- [ ] Loaded automatically when entity tab is opened

---

## 8. Advanced Filter Mode

- [ ] "Advanced" toggle in filter bar expands SQL filter expression input (Monaco, `sql` language mode)
- [ ] Filter passed to `ServiceBusReceiver` as server-side SQL filter on peek
- [ ] Validation: show inline error if SQL is malformed (catch `ArgumentException` from SDK)

---

## 9. Export

- [ ] Selected rows → `[Copy as JSON]` → copies array of `SbMessage` objects to clipboard
- [ ] All peeked rows → `[Export CSV]` → downloads file with all message metadata (no body for large exports)
- [ ] Single message body → `[Copy body]` button in details pane

---

## Acceptance Criteria (Phase 2 Complete)

- [ ] Can select 20 DLQ messages and bulk-resubmit with progress tracking
- [ ] Can compose a message with custom properties and send to a queue in Dev
- [ ] Can save a composed message as a template named "CreateOrder"
- [ ] Can create a scenario "Happy path order" with 3 steps and run it against Dev
- [ ] Favorites show live DLQ counts and update every 30s
- [ ] Auto-refresh at 30s interval keeps queue stats current
- [ ] Filter state is remembered when tab is closed and reopened
