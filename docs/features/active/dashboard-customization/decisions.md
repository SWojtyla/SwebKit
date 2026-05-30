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
