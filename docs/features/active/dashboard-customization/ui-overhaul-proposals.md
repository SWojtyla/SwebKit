# Dashboard Visual Overhaul Proposals

## Purpose

Document two complete dashboard redesign directions for SwebKit before implementation begins. Both proposals assume the current dashboard registry, per-tile persistence, bounded refresh model, and route-first drill-through remain the architectural base.

## Shared Design Goals

- Feel closer to a Power BI workspace than to a landing page.
- Make the first screen useful within five seconds for both scanning and action.
- Preserve SwebKit's existing area model so every tile can drill into a real page or resource snapshot.
- Treat customization as a first-class operator workflow rather than a hidden settings panel.
- Stay responsive from wide desktop windows down to narrow snapped windows without becoming a long stack of identical cards.
- Keep dense operational information readable through layout, hierarchy, and conditional emphasis instead of loud chrome.

## Shared Platform Assumptions

- Tile identities remain registry-driven and persist through `UiStateRepository`.
- Existing tile footprints (`1x1`, `2x1`, `2x2`, `3x2`) remain the minimum supported layout model.
- A future advanced layout mode may add `4x2` analytic bands for trend-heavy tiles, but only if the board keeps backward compatibility with current payloads.
- Drill-through continues to use shell routes, area navigation events, and `OperatorWorkspaceService` snapshots.
- Network-backed tiles keep bounded refresh budgets and independent loading or stale states.

## Proposal A - Power Grid Command Center

### Design Summary

This option leans hardest into the user's Power BI request. The dashboard becomes an analytics cockpit: a persistent filter bar at the top, a dense metric-and-visual board in the center, and an inspector rail for details, saved views, and quick actions.

The visual tone should feel premium and analytical rather than dashboard-generic: pale canvas, dark data ink, subtle grid rhythm, crisp typography, restrained area color, and small but deliberate motion when tiles refresh or change state.

### Information Architecture

```mermaid
flowchart TD
    A[Global slicer bar\nprofile | environment | time window | live mode | search] --> B[Priority KPI ribbon]
    A --> C[Main analytics grid]
    C --> D[Health and risk matrix]
    C --> E[Trend and backlog tiles]
    C --> F[Workspace resume tiles]
    C --> G[Actionable queue tiles]
    C --> H[Narrative insights strip]
    C --> I[Right insight dock\nfilters | drill details | saved views]
```

### Layout Model

- Top slicer bar: global profile selector, environment selector, time range, live toggle, saved view switcher, and command-search entry.
- KPI ribbon: 4 to 8 compact executive tiles for "what needs attention now".
- Main analytics grid: a 12-column board where tiles can span `1x1`, `2x1`, `2x2`, `3x2`, and optionally `4x2` in advanced mode.
- Insight dock: collapsible right-side drawer showing selected-tile context, secondary metrics, filters, recent drill-through targets, and quick commands.
- Navigation rail: left rail remains for primary app areas, but visually de-emphasized so the board feels like the home workspace.

### Signature Widgets

| Widget                | Purpose                                                                       | Suggested sizes     | Notes                                                                         |
| --------------------- | ----------------------------------------------------------------------------- | ------------------- | ----------------------------------------------------------------------------- |
| Priority KPI          | Single urgent metric such as dead letters, failed deployments, unhealthy pods | `1x1`, `2x1`        | Supports thresholds, sparkline, stale indicator, and one-click drill-through. |
| Health Matrix         | Compare Service Bus, AKS, Redis, Pipelines, Observability in one tile         | `2x2`, `3x2`        | Uses conditional color and status chips instead of separate cards.            |
| Backlog Trend         | Show queue depth or pending approvals over time                               | `2x1`, `3x2`, `4x2` | Adds sparkline, change delta, and forecast direction.                         |
| Deployment Confidence | Blend approvals, failed runs, and live runtime alerts                         | `2x2`, `3x2`        | Becomes the closest thing to a release readiness tile.                        |
| Watchlist Table       | Top watched entities, namespaces, or apps                                     | `2x2`, `3x2`        | Sortable mini table with inline severity markers.                             |
| Activity Tape         | Horizontal, fast-scanning event strip                                         | `2x1`, `3x2`        | Better than a raw vertical feed for a BI-style board.                         |
| Workspace Resume      | Favorites, recent resources, open tabs, draft investigations                  | `2x2`, `3x2`        | Works as the operator's continuity panel.                                     |
| Insight Narrative     | LLM-free or future AI-assisted textual summary of key changes                 | `2x1`, `3x2`        | Optional tile that converts multiple metrics into a short situational brief.  |

### Customization Model

- Saved views: operators can save named dashboard scenes such as `Morning Triage`, `Release Window`, `AKS Focus`, or `Messaging Focus`.
- Global slicers: profile, environment, area, severity, and time range can optionally propagate to compatible tiles.
- Tile variants: one template can have multiple instances with different targets, thresholds, and visual modes.
- Footprint-aware rendering: the same widget shows a number-only compact mode at `1x1`, adds context at `2x1`, and exposes richer lists or trends at `2x2` and above.
- Role presets: starter layouts for platform engineer, messaging operator, release manager, and observability engineer.
- Personalization options: density, tile title visibility, compact icons, board background style, dock position, and whether the KPI ribbon is pinned.

### Responsive Behavior

- Wide desktop: 12-column grid with persistent insight dock.
- Standard laptop: 8-column grid, dock collapses to flyout, KPI ribbon reduces to two rows when needed.
- Tablet or narrow snapped window: 4-column grid, slicer bar becomes horizontally scrollable chips, dock becomes a bottom sheet.
- Mobile-like width: one-column story mode with swipable board sections and a sticky action strip for filters and saved views.

### Strengths

- Strongest match for the Power BI reference.
- Makes SwebKit feel like an operational analytics product rather than a utility launcher.
- Reuses the current tile registry and persistence model well.
- Naturally supports future trend, comparison, and observability-heavy widgets.

### Tradeoffs

- Higher visual-system effort because the board needs more deliberate chart, matrix, and trend styling.
- Requires clear rules for global filters so tiles do not become inconsistent.
- Easy to overbuild unless the first slice stays disciplined around a small KPI and trend set.

### Implementation Fit

- Best fit for the current widget registry.
- Should be the primary recommendation if only one redesign ships first.
- First implementation slice should start with the slicer bar, KPI ribbon, new shared tile frame, and 4 to 6 high-value tiles.

## Proposal B - Ops Atlas Workbench

### Design Summary

This option is more experimental. Instead of feeling like a BI report canvas, the dashboard becomes a situation room made of modular zones: a live operations map, a decision lane, a timeline lane, and a compact workbench dock. It is still data-dense, but more narrative and spatial than Proposal A.

The visual tone should feel bold and cinematic without becoming noisy: layered panels, subtle topo or grid textures, strong sectional composition, and area color used to distinguish operational lanes.

### Information Architecture

```mermaid
flowchart LR
    A[Situation header\nmission status | active incidents | environment posture] --> B[Ops atlas canvas]
    B --> C[Resource constellation]
    B --> D[Timeline river]
    B --> E[Decision stack]
    B --> F[Workbench dock]
    F --> G[Favorites]
    F --> H[Open tabs]
    F --> I[Quick actions]
```

### Layout Model

- Situation header: a wide top band with current posture, notable anomalies, and mode toggles.
- Atlas canvas: the main body is split into 3 named lanes instead of a generic tile board.
- Resource constellation lane: a semi-graph view of watched systems, queues, clusters, services, and their current severity.
- Timeline river lane: live events, approvals, alerts, and activity displayed as time-grouped cards or tracks.
- Decision stack lane: prioritized tasks, blockers, and suggested next actions.
- Workbench dock: compact bottom or right-side strip for favorites, open tabs, and command shortcuts.

### Signature Widgets

| Widget                 | Purpose                                                     | Suggested sizes     | Notes                                                                  |
| ---------------------- | ----------------------------------------------------------- | ------------------- | ---------------------------------------------------------------------- |
| Situation Brief        | One-glance summary of current operational posture           | `2x1`, `3x2`, `4x2` | Combines counts, trend arrows, and textual brief.                      |
| Resource Constellation | Visual map of watched systems and dependencies              | `3x2`, `4x2`        | More spatial than tabular; strongest differentiator for this proposal. |
| Timeline River         | Ordered stream of approvals, alerts, and runtime changes    | `2x2`, `3x2`, `4x2` | Better for storytelling and incident-like workflows.                   |
| Decision Stack         | Explicit queue of tasks, escalations, and next actions      | `2x2`, `3x2`        | Could pull from pending approvals, dead letters, and failures.         |
| Focus Lens             | Expanded detail pane for the selected system or incident    | `2x2`, `3x2`        | Replaces a traditional right inspector in compact states.              |
| Workbench Dock         | Personal workspace continuity with recents, tabs, favorites | `2x1`, `3x2`        | Keeps the atlas from losing day-to-day operator utility.               |

### Customization Model

- Scene-based layouts: users save full "scenes" for workflows like incident response, release watch, queue triage, or AKS recovery.
- Lane priorities: users choose which lanes stay pinned and which collapse on smaller widths.
- Mixed visual modes: some widgets can render as table, timeline, heatmap, or constellation depending on the operator's preference.
- Attention rules: users can promote any watched resource or tile to the situation header when it crosses a threshold.
- Personal workbench: the dock supports pinned commands, watchlists, notes, and last-opened resource sets.

### Responsive Behavior

- Wide desktop: three-lane atlas plus dock.
- Laptop: lanes become stacked sections with quick-jump chips at the top.
- Narrow width: the constellation collapses into a watchlist matrix and the timeline becomes the primary center section.
- Mobile-like width: scene selector plus vertically ordered brief, decision stack, timeline, and workbench cards.

### Strengths

- Most distinctive and "out of the box" direction.
- Better for situational awareness and incident-style workflows than a pure KPI board.
- Creates more room for future incident timeline integration and richer context switching.

### Tradeoffs

- Weaker match for the explicit Power BI comparison.
- Higher implementation and interaction risk because the constellation and lane model need careful UX validation.
- Harder to keep compact on narrow widths without falling back to simpler list views.

### Implementation Fit

- Good strategic concept, but a riskier first ship.
- Best used as an inspiration source for later "scene mode" or an optional alternate dashboard layout.
- Elements worth borrowing even if Proposal A is chosen: situation brief, decision stack, and timeline river.

## Comparison Matrix

| Dimension                             | Proposal A - Power Grid Command Center | Proposal B - Ops Atlas Workbench |
| ------------------------------------- | -------------------------------------- | -------------------------------- |
| Power BI alignment                    | High                                   | Medium                           |
| Executive scan speed                  | High                                   | Medium                           |
| Situational storytelling              | Medium                                 | High                             |
| Reuse of current dashboard primitives | High                                   | Medium                           |
| Implementation risk                   | Medium                                 | High                             |
| Differentiation                       | Medium                                 | High                             |
| Best first ship                       | Yes                                    | No                               |

## Recommendation

Choose Proposal A as the primary implementation direction.

Reasons:

- It satisfies the Power BI comparison directly without forcing a new architecture.
- It preserves the current dashboard investments in tile registry, persisted footprints, and drill-through.
- It gives a clearer first implementation slice: global slicers, KPI ribbon, analytic grid, insight dock, and size-aware widgets.
- Proposal B still contributes valuable ideas that can be merged into Proposal A later, especially the situation brief, timeline river, and scene-based saved layouts.

## Suggested Execution Order

1. Implement Proposal A shell framing: slicer bar, KPI ribbon, analytic grid, and collapsible insight dock.
2. Convert the current core widgets into size-aware BI-style tiles with compact, medium, and rich modes.
3. Add saved views, role presets, and propagated global filters.
4. Borrow Proposal B's decision stack and timeline river as advanced tiles after the base board is stable.

## Acceptance Signals For The Chosen Overhaul

- A new user can understand the state of messaging, AKS, Redis, pipelines, and observability from the home screen without opening each area.
- The board feels intentionally composed at desktop, snapped, and narrow widths.
- Customization is fast enough that users actually maintain multiple saved layouts.
- Large tiles add meaning rather than just taking more space.
- Every tile can answer three questions quickly: what changed, how bad is it, and where do I go next.
