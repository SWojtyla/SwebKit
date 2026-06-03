# Dashboard Customization Decisions

## Decision 001 - Rebuild the dashboard UI as a widget board

**Status:** Accepted

**Date:** 2026-05-30

### Context

The first dashboard customization implementation proved the registry, persistence, custom tile instance, bounded refresh, and drill-through contracts. The visual layer still behaves like an operations-console grid and has already needed several compensating fixes for sparse layouts, list tiles, and size controls.

The desired product direction is a phone home-screen style dashboard: responsive widgets, easy configuration, clear tile sizes, and strong behavior across window widths.

### Decision

Rebuild the dashboard presentation as a responsive widget board while preserving the existing data and persistence architecture.

Keep:

- `DashboardTileRegistry` as the source of tile identity and metadata.
- `UiStateRepository` as the shell-local persistence boundary.
- Custom tile instance IDs for resource-specific watches.
- Bounded per-tile refresh behavior.
- `OperatorWorkspaceService` drill-through snapshots.

Change:

- Replace vague size labels with explicit widget footprints such as `1x1`, `2x1`, `2x2`, and `3x2`.
- Give each tile a shared widget frame and size-aware content renderer.
- Move configuration into a consistent drawer/panel flow for all tile types.
- Treat drag-and-drop as optional polish after keyboard and button-based layout editing works well.
- Make visual elegance a first-order requirement: calm density, refined spacing, restrained color, strong typography hierarchy, and polished interactive states.

### Consequences

- The next implementation should focus on the dashboard UI shell and tile rendering contracts, not new integration data.
- Existing persisted `small`, `medium`, and `wide` sizes need a backward-compatible mapping into the new footprint model.
- Manual responsive validation becomes mandatory because the core value of the redesign is layout behavior.
- Visual review is part of acceptance, not polish. A technically correct widget grid is not done if it feels cluttered, noisy, or awkward at normal desktop sizes.

### Alternatives Considered

- Continue incrementally patching the current grouped dashboard layout. Rejected because the layout problems are structural and would keep producing one-off exceptions.
- Restart the whole dashboard feature including persistence and providers. Rejected because the non-visual architecture is useful and already validated by build and persistence tests.

## Decision 002 - Use Power Grid Command Center as the implementation baseline

**Status:** Accepted

**Date:** 2026-06-02

### Context

The feature now has two full visual-overhaul proposals:

- `Power Grid Command Center` — closest to a Power BI-like analytical workspace.
- `Ops Atlas Workbench` — more experimental and scene-oriented.

The user asked for a total dashboard revamp that is more Power BI alike, visually stronger, more useful, and highly customizable.

### Decision

Use `Power Grid Command Center` as the execution baseline for the next implementation plan.

Adopt these as first-class requirements:

- global slicer bar
- KPI ribbon
- analytic grid with responsive widget footprints
- shared BI-style widget frame
- collapsible insight dock
- saved dashboard views

Keep selected ideas from `Ops Atlas Workbench` as follow-on candidates rather than mixing both concepts into the first ship.

### Consequences

- The next plan should split work into shell framing, widget-frame migration, saved-view persistence, and targeted widget upgrades.
- Existing dashboard widgets will be retained and upgraded rather than replaced wholesale.
- The persistence model needs to evolve from one flat tile list toward saved views without breaking current payloads.
- The dashboard will start reading more like an analytical workspace than a phone-home-screen widget board.

### Alternatives Considered

- Implement `Ops Atlas Workbench` first. Rejected because it is a weaker match for the Power BI reference and carries higher interaction risk.
- Blend both proposals immediately. Rejected because it would weaken the first implementation slice and blur the visual hierarchy.
