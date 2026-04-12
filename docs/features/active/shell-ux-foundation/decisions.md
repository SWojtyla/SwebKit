# Decisions - shell-ux-foundation

---

title: "Decisions - shell-ux-foundation"
owner: "GitHub Copilot"
status: "Planned"

---

## Decision 001 - Derive shell context from the route, not from mutable click state

**Status:** Accepted

**Date:** 2026-04-12

### Context

`MainLayout` currently tracks `CurrentArea` through shell navigation and events. That is good enough for click-driven transitions but fragile for direct-route entry, browser-like navigation, and future workspace restore flows.

### Decision

Move toward a route-derived shell context model so nav state, top-bar context, and page identity come from routing rather than from a mutable shell field alone.

### Consequences

- Direct navigation and workspace restore become more reliable.
- Shell state becomes easier to reason about and test.
- Existing refresh and event flows need to be adjusted so they still target the active routed area correctly.

### Alternatives considered

- Alternative A - keep `CurrentArea` as the primary source of truth: rejected because it will drift as the shell becomes more complex.

---

## Decision 002 - Standardize page chrome with shared primitives instead of bespoke page headers

**Status:** Accepted

**Date:** 2026-04-12

### Context

Top-level pages already mix bespoke headers, `PageToolbar`, and different heading levels. This undermines both visual consistency and route focus behavior.

### Decision

Use one shared page-header contract and shared empty/loading/error structure for routed pages, while still allowing page-specific content inside that structure.

### Consequences

- Pages feel like part of one product.
- Shared accessibility and focus behavior becomes easier to verify.
- Some existing page-level markup will need refactoring instead of one-off cosmetic tweaks.

### Alternatives considered

- Alternative A - leave each page to style its own header: rejected because it preserves the current inconsistency.

---

## Decision 003 - Surface production context at shell level, not only in destructive dialogs

**Status:** Accepted

**Date:** 2026-04-12

### Context

Production safety currently appears mainly inside page-level destructive flows. That helps at the moment of confirmation, but it does not give the operator persistent awareness of environment risk.

### Decision

Add shell-level production context so the operator can tell they are in a production-marked environment before they reach a destructive action.

### Consequences

- Safety becomes proactive rather than reactive.
- The shell must balance persistent clarity with visual noise.

### Alternatives considered

- Alternative A - keep production cues only inside confirmation dialogs: rejected because it is too late in the interaction.

---

## Decision 004 - Treat notification-center polish as part of shell trust, not as a standalone feature

**Status:** Accepted

**Date:** 2026-04-12

### Context

Notifications already exist through toast and history components, but their current presentation is part of the broader shell trust problem.

### Decision

Keep notification-center refinement inside the shell foundation rather than splitting it into a separate feature.

### Consequences

- Shell trust can be improved in one cohesive pass.
- The scope stays manageable because the feature is not adding new notification domains, only polishing current shell behavior.

### Alternatives considered

- Alternative A - create a dedicated notification feature: rejected because that would delay baseline shell quality without adding new operator value.
