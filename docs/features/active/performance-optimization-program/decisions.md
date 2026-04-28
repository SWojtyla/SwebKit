# Decisions - performance-optimization-program

---

title: "Decisions - performance-optimization-program"
owner: "GitHub Copilot"
status: "Accepted"

---

## Decision 001 - Treat SwebKit as a Blazor Hybrid performance problem first

**Status:** Accepted

**Date:** 2026-04-28

### Context

The app is hosted in .NET MAUI, but the actual operator experience is primarily a Blazor UI running inside a WebView. Generic native MAUI guidance about deep XAML layout trees and compiled bindings does not explain the highest-probability bottlenecks in this codebase.

### Decision

Prioritize startup asset loading, Blazor rerender control, repeated component patterns, and high-frequency event behavior over native MAUI layout micro-optimizations.

### Consequences

- Performance work will focus first on `wwwroot/index.html`, layout cascades, shared render primitives, and the heaviest Blazor workspaces.
- Native MAUI project and packaging settings are still in scope, but later.

### Alternatives considered

- Optimize native MAUI/XAML layout usage first - rejected because the native host is intentionally thin.
- Start with publish-time trimming/AOT first - rejected because measured behavior gains should come before risky packaging experiments.

---

## Decision 002 - Sequence the work in waves instead of a single cross-app sweep

**Status:** Accepted

**Date:** 2026-04-28

### Context

The app has several large feature areas and multiple classes of performance issues: startup, rendering, repeated lists, JS interop, and publish footprint. A single broad refactor would be hard to validate and likely to drift.

### Decision

Split the program into four waves: startup baseline and shell, heavy workspaces, interaction hot paths, and publish-time optimization.

### Consequences

- Each slice can be validated independently.
- The highest-ROI wins land first.
- Late-wave experiments can be dropped if earlier work solves the user-facing problem sufficiently.

### Alternatives considered

- One large end-to-end optimization pass - rejected because it increases regression risk and makes attribution difficult.

---

## Decision 003 - Use existing shared render gates as the default optimization mechanism

**Status:** Accepted

**Date:** 2026-04-28

### Context

SwebKit already has shared render-control primitives in `SwebKitComponentBase` and `SwebKitLayoutBase`. These provide a consistent way to coalesce rerenders and suppress unnecessary shell-wide redraws.

### Decision

Prefer extending or consistently applying the existing shared render-gate patterns before introducing one-off `ShouldRender`, `IHandleEvent`, or manual parameter-setting logic.

### Consequences

- Optimization work stays aligned with current app architecture.
- Feature-specific code remains easier to reason about.
- More invasive Blazor performance techniques remain reserved for measured hotspots.

### Alternatives considered

- Adopt aggressive per-component `ShouldRender` overrides everywhere - rejected because it raises stale-UI risk.
- Apply manual `SetParametersAsync` widely - rejected because the complexity is not justified without measurement.

---

## Decision 004 - Make Observability the first heavy-workspace optimization target

**Status:** Accepted

**Date:** 2026-04-28

### Context

Observability combines route-level orchestration, multiple mounted tabs, charts, editor assets, auto-refresh, and child component trees. It has the highest concentration of likely rendering and startup-adjacent costs.

### Decision

After startup/shell work, optimize Observability before Service Bus, Pipelines, AKS, Storage, Redis, and Incident Timeline.

### Consequences

- Monaco lazy loading becomes an early deliverable.
- Tab mounting, keep-alive, and child rerender scope become early validation cases.

### Alternatives considered

- Start with AKS - rejected because AKS already contains some optimization work such as virtualization and batched log rendering.
- Start with forms/settings - rejected because they are unlikely to drive the main user complaint.

---

## Decision 005 - Use working budgets and slice exit criteria before coding each wave

**Status:** Accepted

**Date:** 2026-04-28

### Context

The original plan identified the right areas but did not yet force each implementation slice to prove a measurable or clearly perceived improvement. Without budgets and exit criteria, the team could drift into broad refactors that are hard to validate.

### Decision

Every slice must start from a baseline, target one primary bottleneck class, and close only when before and after evidence plus regression checks are recorded.

### Consequences

- The status file and test plan become active execution controls, not passive summaries.
- Small slices are favored over broad, mixed optimization batches.
- Publish-time work cannot jump ahead of behavior-level wins.

### Alternatives considered

- Use qualitative guidance only - rejected because it leaves too much room for unmeasured churn.

---

## Decision 006 - Prefer route-local asset loading over global script loading for heavy optional features

**Status:** Accepted

**Date:** 2026-04-28

### Context

The current boot path pays startup cost for assets that are only required by specific heavy feature routes, especially Observability Logs. This violates the two-phase startup intent described in the architecture and design docs.

### Decision

Optional heavy assets should load as close as practical to the feature that needs them, provided the first-use state is explicit and interop timing remains safe.

### Consequences

- `index.html` should be reserved for shell-critical assets.
- Heavy route features such as Monaco should own their first-use loader and loading state.
- First-use validation becomes mandatory for every deferred asset.

### Alternatives considered

- Keep all third-party scripts global for simplicity - rejected because it keeps paying startup cost for non-startup-critical features.
