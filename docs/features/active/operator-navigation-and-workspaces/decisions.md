# Decisions - operator-navigation-and-workspaces

---

title: "Decisions - operator-navigation-and-workspaces"
owner: "GitHub Copilot"
status: "Planned"

---

## Decision 001 - Persist recents locally, but keep favorites and named workspaces environment-scoped

**Status:** Accepted

**Date:** 2026-04-12

### Context

The codebase already splits durable environment data (`profiles.json`) from local shell/UI state (`ui-state.json`). Favorites and named workspaces are operator-curated environment context, while recents are mostly local convenience history.

### Decision

Keep recent resources in UI state, but store favorites and named workspaces as environment-scoped durable data alongside other profile configuration.

### Consequences

- Recents stay lightweight and machine-local.
- Favorites and named workspaces can travel with the environment profile and remain meaningful after restart.

### Alternatives considered

- Alternative A - keep everything in UI state: rejected because curated favorites/workspaces are more durable than ephemeral shell history.

---

## Decision 002 - Workspace snapshots store semantic page context, not raw component objects

**Status:** Accepted

**Date:** 2026-04-12

### Context

Current page and tab services can hold object state, but that is not a safe durable format for versioned restore across app sessions.

### Decision

Workspace snapshots should persist semantic route, resource, filter, and contributor payload data only. They should never serialize live component objects or app-service object graphs.

### Consequences

- Workspace payloads become versionable and testable.
- Restore flows need contributor contracts and normalization logic instead of shortcut object reuse.

### Alternatives considered

- Alternative A - serialize current tab/page objects directly: rejected because it is brittle and unsafe across versions.

---

## Decision 003 - Replace one-off palette resource branches with provider-based search

**Status:** Accepted

**Date:** 2026-04-12

### Context

The command palette already supports command search plus an ad hoc `go ` mode that manually assembles some resource results. That does not scale as more areas join the shell.

### Decision

Move to provider-based resource search and one ranking pipeline instead of continuing to add special-case branches inside `CommandPalette.razor`.

### Consequences

- New capability areas can join search without editing one central if-statement per area.
- Ranking, recents, and favorites can apply across one result model.

### Alternatives considered

- Alternative A - keep growing the current `go ` implementation: rejected because it will become harder to maintain and test.

---

## Decision 004 - Named workspaces complement existing tab persistence instead of replacing it

**Status:** Accepted

**Date:** 2026-04-12

### Context

`TabService` already persists transient open tabs and pin state. That is useful session continuity, but it is not enough to express named, reusable investigation workspaces.

### Decision

Keep transient tab persistence as-is conceptually and add named workspaces above it rather than replacing the tab model.

### Consequences

- Session continuity and named workspaces serve different operator needs without fighting for the same abstraction.
- Restore flows can reuse some tab concepts while keeping durable workspace semantics explicit.

### Alternatives considered

- Alternative A - replace tabs with workspaces entirely: rejected because tabs and named workspaces solve different problems.
