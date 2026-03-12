# Decisions - AKS Enhancements v2

---

title: "Decisions - AKS Enhancements v2"
owner: ""
status: "Planned"
created: "2026-03-11"

---

## Decision 001 — Custom HTML context menus instead of browser native

**Status:** Accepted

**Date:** 2026-03-11

### Context

Inline action buttons on resource rows add visual noise and limit the number of actions that can be shown. Right-click context menus are the standard UX pattern for row-level actions in data grids.

### Decision

Build a custom `ContextMenu.razor` component using HTML/CSS positioned at the cursor. Suppress the browser's native context menu on resource rows. Dismiss on click-outside, Escape, or scroll.

### Consequences

- Works reliably in MAUI BlazorWebView (browser native context menus are inconsistent in WebView).
- Full control over styling and behavior.
- Must handle z-index, viewport boundary clipping, and keyboard navigation.

### Alternatives considered

- Browser native context menu via `contextmenu` event — rejected due to MAUI WebView inconsistencies.
- Fluent UI menu component — considered, but the overhead of a full menu system is unnecessary for this use case.

---

## Decision 002 — Production guard for destructive operations

**Status:** Accepted

**Date:** 2026-03-11

### Context

Mutative operations like pod deletion, deployment restart, and Helm rollback can cause service disruption if performed accidentally on production clusters.

### Decision

All destructive actions show an inline confirmation bar (not a modal). Production environments additionally require typing the resource name to confirm. The `ProjectEnvironment.IsProduction` flag determines the guard level.

### Consequences

- Reduced risk of accidental production changes.
- Slightly slower workflow for production — acceptable tradeoff for safety.
- Non-production environments still get a simple confirm/cancel prompt.

### Alternatives considered

- Modal confirmation dialogs — rejected for flow disruption.
- No confirmation on non-production — rejected; even dev cluster changes should be intentional.

---

## Decision 003 — Helm rollback via CLI subprocess

**Status:** Accepted

**Date:** 2026-03-11

### Context

The Helm Go SDK does not expose rollback as a library call usable from .NET. The Kubernetes client library can read Helm release secrets but cannot perform rollback natively.

### Decision

Invoke `helm rollback <release> <revision> -n <namespace>` as a CLI subprocess. Require `helm` binary on PATH. Fail gracefully with a clear error if `helm` is not found.

### Consequences

- Simple and reliable — uses the same mechanism as manual rollback.
- Requires `helm` CLI installed — acceptable for developer tooling.
- Subprocess output captured for error reporting.

### Alternatives considered

- Reconstruct rollback by copying old Secret to new revision — rejected as fragile and non-standard.
