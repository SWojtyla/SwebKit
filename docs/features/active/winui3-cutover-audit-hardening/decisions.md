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