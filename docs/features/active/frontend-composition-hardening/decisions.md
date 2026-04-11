# Decisions - frontend-composition-hardening

---

title: "Decisions - frontend-composition-hardening"
owner: "GitHub Copilot"
status: "Planned"

---

## Decision 001 - Keep the feature scoped to hardening existing shell and page composition

**Status:** Accepted

**Date:** 2026-04-11

### Context

The verified issues span several areas of the frontend, and there are other pages in the app that still construct concrete clients directly. Fixing every instance in one feature would turn a maintainability pass into a whole-app rewrite.

### Decision

This feature is limited to `MainLayout`, `ObservabilityPage` and `ObservabilityLogs`, `ServiceBusPage`, and the AKS bootstrap path. It does not include Dashboard, Storage, Redis, or a broader frontend redesign.

### Consequences

- The work remains incremental and reviewable.
- The highest-churn operational pages are improved first.
- Other pages with similar issues remain explicit follow-up work instead of hidden scope creep.

### Alternatives considered

- Alternative A - Clean up every page that constructs a client directly. Rejected because the scope is too broad for one active feature.
- Alternative B - Limit the work to shell-only changes. Rejected because the most expensive maintainability issues live in the page composition paths.

---

## Decision 002 - Keep page orchestration in SwebKit.App and move only creation contracts into SwebKit.Core

**Status:** Accepted

**Date:** 2026-04-11

### Context

The architecture already separates UI orchestration in `SwebKit.App` from shared abstractions in `SwebKit.Core` and concrete integrations in separate projects. A maintainability refactor could easily blur that boundary if page state or Razor-specific concerns are moved into Core.

### Decision

Keep page coordinators and shell presentation behavior in `SwebKit.App`. Add only the small creation contracts needed for provider and client activation to `SwebKit.Core`. Keep concrete implementations outside Razor pages and outside Core domain code.

### Consequences

- The refactor aligns with the current architecture instead of creating a new layering model.
- Pages become thinner without pushing UI concerns into Core.
- Concrete infrastructure types stop leaking into Razor pages.

### Alternatives considered

- Alternative A - Move page coordinator logic into `SwebKit.Core` services. Rejected because the logic is UI-composition specific.
- Alternative B - Keep all abstractions in `SwebKit.App` only. Rejected because creation seams for infrastructure clients belong with the shared contract layer.

---

## Decision 003 - Replace timing-based drill-through with explicit readiness handoff

**Status:** Accepted

**Date:** 2026-04-11

### Context

`ObservabilityPage` currently uses a timing delay to switch to the Logs tab and then invoke the child component. That makes behavior render-timing dependent and harder to test deterministically.

### Decision

Replace the delay-based drill-through with an explicit render-ready handoff between the page and the logs surface. The implementation can be a pending request model, a tab-ready callback, or a small coordinator, but it must not rely on timing sleeps.

### Consequences

- The drill-through becomes deterministic and easier to test.
- Future changes to the logs component lifecycle are less likely to introduce flaky behavior.
- The page retains the current UX while removing a known fragile pattern.

### Alternatives considered

- Alternative A - Keep the timing delay and raise it slightly. Rejected because it stays nondeterministic and environment-sensitive.
- Alternative B - Merge more logs behavior directly into the page. Rejected because it increases page size and coupling.

---

## Decision 004 - Surface shell async failures through a shared user-visible path plus ILogger

**Status:** Accepted

**Date:** 2026-04-11

### Context

`MainLayout` background initialization and keyboard shortcut registration currently degrade into console output. In a desktop app, that is too easy to miss and does not help operators understand why shell behavior changed.

### Decision

Route actionable shell async failures through a shared shell error presenter and structured logger. Continue to let cancellation flow normally instead of turning it into error noise.

### Consequences

- Operators get a visible signal when shell startup behavior is degraded.
- Diagnostics improve without forcing every page to invent its own shell error handling.
- The feature preserves the current startup flow instead of replacing it.

### Alternatives considered

- Alternative A - Keep console-only reporting. Rejected because it hides user-visible degradation.
- Alternative B - Fail the whole shell on any startup integration error. Rejected because the app should stay usable when degradation is partial.

---

## Decision 005 - Prefer composition-level bUnit coverage over broad new E2E coverage

**Status:** Accepted

**Date:** 2026-04-11

### Context

The verified testing gap is weak shell and composition coverage, not a lack of high-level end-to-end scenarios. Broad E2E expansion would raise cost and brittleness before the new seams are even in place.

### Decision

Prioritize bUnit and targeted registration tests for `MainLayout`, Observability drill-through, Service Bus bootstrap, AKS bootstrap, and demo-mode selection. Add new E2E only if a shell behavior cannot be asserted reliably at the component level.

### Consequences

- The new seams remain cheap to validate as they evolve.
- The test suite targets the actual maintainability risk.
- E2E growth stays intentional instead of default.

### Alternatives considered

- Alternative A - Lead with new E2E coverage. Rejected because it adds cost before the underlying seams are testable.
- Alternative B - Rely only on manual validation. Rejected because it would not close the current shell and composition regression gap.

---

## Decision 006 - Preserve current strengths as explicit invariants

**Status:** Accepted

**Date:** 2026-04-11

### Context

The frontend already has strong patterns worth keeping: `SwebKitComponentBase` encapsulates useful load and error behavior, `PageDataCache` improves navigation feel, and several components already respect cancellation and lifecycle constraints.

### Decision

Treat `SwebKitComponentBase` behavior, `PageDataCache` snapshot behavior, and cancellation-first request handling as invariants for this feature. The refactor should reduce page weight without discarding these strengths.

### Consequences

- The feature improves maintainability without resetting proven patterns.
- Regression review has clear success criteria.
- The implementation stays focused on root causes instead of rewriting good existing behavior.

### Alternatives considered

- Alternative A - Replace current base and cache patterns during the same feature. Rejected because it expands scope and risk.
- Alternative B - Ignore the preserved strengths and only chase page-size reduction. Rejected because it would likely cause regressions.