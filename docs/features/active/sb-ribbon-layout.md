# Service Bus ribbon layout — compact command surface, visible filters, scheduled state

State: In Progress

## Goal

Replace the stacked toolbars + overflow "Actions" menu with a compact ribbon
(prototype `docs/prototypes/service-bus/ribbon.html`), surface active filters as
chips, and make **Scheduled** a first-class message state next to Active and DLQ.

## What shipped

### 1. Ribbon (`SbRibbon`)

- Four tabs: **Home** (compose/batch-send/scheduled/templates, entity search,
  ask-AI), **Messages** (refresh, peek count, auto-refresh, filters on/off,
  rules, add-rule, saved filters, export ZIP), **DLQ & Recovery** (batch replay,
  reach, DLQ triage, replay-to — plus Purge in a separated danger group),
  **View** (entity tree, columns, density, NSB mode).
- Compact icon+label controls (`RibbonButton`/`RibbonSelect`) with group labels
  and hairline separators; Compose is the only filled-primary action.
- Tab click toggles collapse; a chevron button does the same. State persists in
  `localStorage` under `sb-ribbon-state`.
- Namespace picker stays in the tab strip (context is global, not per-tab).
- The old `sb-actions-menu` overflow menu is gone; every testid it held moved
  onto a ribbon button unchanged.

### 2. Filter chips bar

- Always-visible strip inside `MessageList` (outside the ribbon so a collapsed
  ribbon can never hide active filters): text search input, one removable chip
  per narrowing condition (`search "x"`, `session:id`, one per configured rule
  via `describeRule`), `+ rule`, `Clear all`, and the `X of Y` match count.
- A `filters off — showing all` amber chip appears when rules exist but the
  master toggle is off — previously the list would silently show everything.

### 3. Scheduled as a state

- `SbMessage.ScheduledEnqueueTime` (nullable `DateTimeOffset?` →
  `scheduledEnqueueTime` JSON) mapped from
  `ServiceBusReceivedMessage.ScheduledEnqueueTime`; `MinValue` normalizes to
  `null`.
- `view=scheduled` reuses the active peek (scheduled messages live in the main
  queue until they fire) and keeps rows where
  `isScheduledMessage(m)` — stamp set _and_ in the future. Load-more and
  `totalAvailable` use `stats.scheduledMessageCount`.
- Table gains a **State** column (Active / Scheduled / Dead-lettered) and a
  **Scheduled for** column; both are force-included on the scheduled view and
  toggleable elsewhere.
- `MessageDetail` shows a scheduled badge + fire time, swaps the settle action
  for a destructive **Cancel schedule** button, and lists Scheduled For on the
  system tab.
- Entity tree badges: the scheduled count is a clickable amber badge that opens
  the entity straight into the scheduled view.
- Demo: `ScheduleMessageAsync` stores the message in the active collection with
  the stamp (peek returns it, like the broker); `CancelScheduledMessageAsync`
  removes it; `ScheduledMessageCount` counts unfired stamps and
  `ActiveMessageCount` excludes them — same accounting as
  `QueueRuntimeProperties`. New `order-scheduled` queue carries 2 active + 2
  scheduled seeds.
- **Receiver honesty**: `IsReceivable` (stamp absent or already fired) guards
  every receive-style op — complete, manual dead-letter, purge, park-for-reach,
  active-side resubmit. Scheduled messages survive a purge and can't be walked
  by a reach operation, matching the broker.
- Limit: the scheduled view shows scheduled messages _inside the peeked
  window_; a deep one beyond it stays unreachable (the SDK has no
  scheduled-only peek).

### 4. Typed + relative filters

- New fields: `message-id`, `subject`, `correlation-id` (text ops),
  `scheduled-time` (date ops).
- Text gains `starts-with`; numeric and date gain `between` (stored as
  `min,max` so saved filters stay flat — `splitRange`/`isRangeOperator` own the
  encoding).
- Date gains `older-than` / `within-last` taking a duration `N(m|h|d)` (bare
  number = minutes); the rule builder edits them as number+unit and `between`
  as two bound inputs.
- Unparseable values never throw — they match nothing.

## Non-goals

- Broker-side scheduled-only peek (SDK has none).
- Editing a scheduled message's fire time (cancel + reschedule stays in the
  Scheduled panel — unchanged).
- Cross-entity scheduled aggregation.

## Validation results

- `dotnet test` SwebKit.Core.Tests: **968/968** (incl. 4 new
  `DemoServiceBusClientScheduledTests`).
- `dotnet test` SwebKit.Sidecar.Tests ~ServiceBus: **97/97**.
- vitest: **818/818** (21 in `filterLogic.test.ts` covering every operator
  family, durations, ranges, scheduled-time nulls, `isScheduledMessage`).
- Playwright `service-bus.spec.ts`: **42/42** — all existing specs re-pointed
  from `sb-actions-menu` to ribbon tabs; 4 new (scheduled view, ribbon
  tabs/collapse, filter chips, relative-time rule).
- `tsc -b` clean; eslint 0 errors (2 warnings, both pre-existing patterns).

## Risks (blunt)

1. Scheduled messages beyond the peek window are invisible — surfaced via the
   honest `X of N scheduled` footer count rather than pretending completeness.
2. `now` in filters drifts between evaluations — acceptable; relative ops
   re-evaluate per render, saved filters keep the duration (not an instant).
3. Ribbon collapse must not hide active filter indicators — chips bar lives
   outside the ribbon body.

