# Decisions - style-system-polish-9

---

title: "Decisions - style-system-polish-9"
owner: ""
status: "Review"

---

## Decision 001 - Define 9/10 as measurable drift reduction, not perfection

**Status:** Accepted

**Date:** 2026-06-14

### Context

The first style-system feature moved the app from roughly 6/10 to a review-ready 7.5/10 by adding primitives and migrating representative API Client, AKS, and Service Bus surfaces. The app still has many raw controls, but not every raw control is automatically bad.

### Decision

Treat 9/10 as a measurable threshold: shared primitives should be the default for repeated controls, remaining raw controls should be intentional exceptions, and inventory counts should move materially below the current baseline.

### Consequences

- Prevents endless churn chasing 0 raw controls.
- Keeps context-menu and row-local controls from being over-abstracted.
- Gives the maintainer a concrete finish line.

### Alternatives considered

- Require all raw buttons/selects to disappear - rejected because context menus, row actions, and third-party wrappers make that counterproductive.
- Treat the first style-system feature as fully done - rejected because inventory still shows clear repeated drift families.

---

## Decision 002 - Migrate by feature area, not by global search-replace

**Status:** Accepted

**Date:** 2026-06-14

### Context

Feature-local styles are protected by Blazor CSS isolation. A global class rewrite can silently lose visual styles unless paired with scoped `::deep` bridges and focused tests.

### Decision

Each implementation slice must own one feature area and include markup, scoped CSS bridge, tests, and inventory update together.

### Consequences

- Lower risk of visual regressions.
- Easier focused validation.
- Slightly slower migration, but much safer for a visual polish feature.
