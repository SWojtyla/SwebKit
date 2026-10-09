# Service Bus — UX layout prototypes

State: **Proposed** — static evaluation mocks, not wired to the app.

Open `index.html` for the comparison table, or each file directly:

| File           | Concept                | One-line idea                                                                                                                          |
| -------------- | ---------------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `zones.html`   | Zones                  | Three labelled regions (Navigate / Browse / Inspect); actions live in the zone they affect                                             |
| `ribbon.html`  | Ribbon                 | Tabbed ribbon (refined: compact icon+label controls); filters as visible chips + typed rule builder; Active/Scheduled/DLQ state picker |
| `minimal.html` | Progressive disclosure | One line of chrome; everything else in overlays/on hover                                                                               |

`ribbon.html` is the direction under iteration. Current additions vs. the app:
a **State** segmented control (Active / Scheduled / DLQ) so scheduled
messages are distinguishable, a visible filter-chip bar, and a rule
builder whose operators adapt to the property type (numeric ≥/≤/between,
time "older than / within last / between", text contains/regex).

## What problem these attack

Current page stacks ~20 controls across three bars:

- **Top bar:** namespace, hide tree, entity search, templates, compose, Actions▾ (6 ops)
- **Entity bar:** breadcrumb + pin + AskAI, Active/DLQ tabs, Purge All
- **List toolbar:** search, saved filters, peek count, auto-refresh, density, Filters on/off, Advanced+rules, clear-all, columns, ZIP, NSB

Result: the message list — the actual payload — gets squeezed, and
same-looking bordered buttons make every action feel equal-weight.

## Evaluation questions

1. Is the entity tree worth permanent space (A, B) or an overlay (C)?
2. Should batch/recovery ops be one level down (A/C menus) or labelled
   and visible (B ribbon)?
3. Does per-message detail belong docked-right, bottom, or unchanged?
4. How much do we hide filter/view prefs — always-on strip (A),
   ribbon tab (B), or one popover (C)?

## Notes

- Dark theme only, app palette, static mock data (orders queue).
- Light JS for tabs/drawers/popovers — evaluate interaction, not visuals.
- No code is shared with `web/` — these are throwaway exploration artifacts.

