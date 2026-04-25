# Decisions - winui3-cutover-audit-hardening

---

title: "Decisions - winui3-cutover-audit-hardening"
owner: ""
status: "Accepted"

---

## Decision 001 — Split parity and hardening out of the baseline migration feature

**Status:** Accepted

**Date:** 2026-04-24

### Context

The original `winui3-migration` feature now spans baseline route delivery, parity closure, structural refactoring, and cutover readiness. That makes the active feature too broad to use as a reliable execution record.

### Decision

Keep `winui3-migration` as the baseline migration checkpoint and move the remaining parity audit, hardening, refactoring, and cutover work into this dedicated follow-up feature.

### Consequences

- The current repo state can be discussed honestly: broad native coverage exists, but readiness is not yet proven.
- Remaining work gets a smaller, more defensible scope with explicit validation gates.
- The original migration feature should stop widening scope.

### Alternatives considered

- Keep all remaining work in `winui3-migration` — rejected because the feature had already become too broad and under-validated.
- Mark `winui3-migration` done immediately — rejected because parity and cutover readiness are still unresolved.

---

## Decision 002 — Treat generated `App.g.i.cs` as a symptom surface only

**Status:** Accepted

**Date:** 2026-04-24

### Context

The active debugger stop is currently visible at the generated `Debugger.Break()` line inside `App.g.i.cs`. Editing generated XAML compiler output would hide the signal without fixing the underlying exception.

### Decision

Do not patch generated files. Investigate the real failing route/action and capture runtime evidence from logs, event traces, and targeted repro steps instead.

### Consequences

- The repo keeps a useful debug signal.
- Exception work is forced onto the real ownership surface: page, view-model, or shared service.

### Alternatives considered

- Disable the generated break hook — rejected because it would mask unhandled exceptions during the migration.
- Patch generated files directly — rejected because the change would be lost on rebuild and would not address root cause.

---

## Decision 003 — Prioritize shared refactors before deeper page expansion

**Status:** Accepted

**Date:** 2026-04-24

### Context

The WinUI host already has shared shell controls and `PageScaffold`, but repeated page activation code and page-local state/detail layouts are still spreading across the routed pages.

### Decision

Use the follow-up feature to complete shared state, metric, and detail primitives plus the repeated initial-load pattern before pushing additional page complexity.

### Consequences

- Later parity work should require less rework.
- Large routed pages become easier to validate and maintain.
- Some visible feature work may slow down briefly while shared structure lands.

### Alternatives considered

- Keep shipping page-local XAML and refactor later — rejected because the duplication is already visible.
- Rewrite the entire WinUI UI layer before any more parity work — rejected because the current baseline is already useful and should be hardened incrementally.

---

## Decision 004 — Split the remaining migration work into feature-specific active plans

**Status:** Accepted

**Date:** 2026-04-25

### Context

The original follow-up feature still grouped layout redesign, settings parity, and all remaining domain work under one umbrella. That was broad enough to hide ordering, ownership, and cutover-critical dependencies.

### Decision

Keep `winui3-cutover-audit-hardening` as the cutover coordination feature only, and move the remaining implementation plan into dedicated active features for layout redesign, settings completeness, Service Bus, AKS, Redis, Storage, Pipelines/Releases, and Observability.

### Consequences

- The repo now has one feature folder per remaining migration slice instead of a single catch-all checklist.
- Dependency order is explicit: layout redesign first, settings completeness second, then the domain parity slices.
- This umbrella can focus on integration evidence and the final cutover recommendation.

### Alternatives considered

- Keep expanding the umbrella with sub-checklists only — rejected because the execution surface would still be too broad.
- Create one new global wave plan — rejected because it would repeat the same coordination problem under a new name.

---

## Decision 005 — Prefer content-first proportions over tall page-header chrome

**Status:** Accepted

**Date:** 2026-04-25

### Context

The current WinUI pages spend too much vertical space on top-of-page header and context bands while the actual operator workspaces remain compressed. The layout redesign needed a global rule, not just a page-local preference, so later parity work would not keep repeating the same proportion problem.

### Decision

Adopt a global content-first layout rule across the remaining WinUI migration work: page headers should stay compact, carry only title, primary actions, and critical live state, and defer secondary context to inline, collapsible, or adjacent surfaces closer to the active workspace.

### Consequences

- The layout redesign now has a concrete proportion target instead of a generic shared-primitives goal.
- Downstream feature plans should treat oversized top-of-page info bands as layout debt, not as the default structure.
- Dashboard, Settings, and later workspaces should expose more visible task content at normal desktop sizes.

### Alternatives considered

- Keep the current tall header structure and only restyle it — rejected because it does not solve the content-density problem.
- Let each page decide how much header chrome it needs — rejected because the problem is global and would otherwise repeat across every feature.
