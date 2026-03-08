# Extra Feature - Explicit Active vs DLQ Mode Selection

## Feature Overview

This pass adds explicit mode-selection controls directly in the Service Bus entity tree so users can intentionally open either Active or DLQ views per entity.

The previous behavior relied on implicit defaults and count badges. Users now get:

- explicit `Active` and `DLQ` actions on queue and subscription rows,
- both Active and DLQ counts visible at the same time,
- default row click preserved as an easy Active shortcut.

## How To Use

1. Open the Service Bus page and expand a namespace.
2. In a queue or subscription row, use:
   - `Active <count>` to open the active-message tab,
   - `DLQ <count>` to open the dead-letter tab.
3. Clicking anywhere else on the row still opens Active mode for fast navigation.

## Example

For queue `orders`:

- `Active 42` opens `orders` active messages.
- `DLQ 3` opens `orders (DLQ)`.
- row click on `orders` opens active messages.

## Technical Notes

- `EntityTree.razor`
  - Adds explicit mode action buttons per queue/subscription row.
  - Adds dual-count display (`Active` and `DLQ`) instead of mutually exclusive count rendering.
  - Adds `OnEntityModeSelected` callback with `(SbEntityInfo Entity, bool IsDlq)` payload.
- `ServiceBusPage.razor`
  - Wires mode callback and opens tabs with mode-specific IDs.
  - Labels DLQ tabs with `(DLQ)` for clear context.
- Tests
  - `EntityTreeTests` verifies dual counts, explicit mode callback behavior, and default row-click behavior.
