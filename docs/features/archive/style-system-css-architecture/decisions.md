# Decisions - style-system-css-architecture

---

title: "Decisions - style-system-css-architecture"
owner: ""
status: "Review"

---

## Decision 001 - Keep `app.css` as the stable entry point

**Status:** Accepted

**Date:** 2026-06-14

### Context

`wwwroot/index.html` links directly to `app.css`. Changing the linked file would add runtime/static asset risk to a structural CSS cleanup.

### Decision

Keep `app.css` linked from `index.html`, but reduce it to ordered `@import` statements for layer files under `wwwroot/styles/`.

### Consequences

- Runtime entry point stays stable.
- The global stylesheet becomes reviewable.
- Future work can migrate individual layers without editing `index.html`.

---

## Decision 002 - Split by ownership and original load order

**Status:** Accepted

**Date:** 2026-06-14

### Context

The global stylesheet mixed tokens, themes, shell, primitives, observability, storage, Service Bus, AKS, and legacy helpers. Moving selectors across order boundaries could change visuals.

### Decision

Split the file by stable macro sections while preserving original selector order through import order.

### Consequences

- Low visual-regression risk.
- Remaining messy legacy areas are isolated into named files for future cleanup.
- The split is not the final selector migration; it is the architecture foundation for that migration.