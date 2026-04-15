# Decisions - aks-runtime-diagnostics-depth

---

title: "Decisions - aks-runtime-diagnostics-depth"
owner: "GitHub Copilot"
status: "In Progress"

---

## Decision 001 - Keep diagnostics on the existing `/aks` route

**Status:** Accepted

**Date:** 2026-04-12

### Context

The AKS page already has the resource-selection model, panel column, and bootstrap flow needed for deeper diagnostics. A second route would duplicate selection state and slow operator movement.

### Decision

Implement the new diagnostics as additive panels and badges on the existing AKS page.

### Consequences

- Keeps one AKS mental model.
- Requires tight layout discipline as more panels are added.
- Reuses the current auto-refresh pause behavior and bootstrap seams.

### Alternatives considered

- Alternative A - Create a second diagnostics route. Rejected because it duplicates selection and navigation state.
- Alternative B - Keep using raw YAML and logs only. Rejected because the current workflow leaves too much interpretive work on the operator.

---

## Decision 002 - Diagnostics stay evidence-based, not root-cause claims

**Status:** Accepted

**Date:** 2026-04-12

### Context

Probe failures, network policies, placement rules, and PDBs are often involved in outages, but the app cannot reliably prove they are the root cause from static object state alone.

### Decision

The feature will present observed diagnostics and likely constraints based on cluster objects and events, but it will not claim definitive root cause.

### Consequences

- Keeps operator trust by avoiding overclaiming.
- Requires explanation text that distinguishes object state from inferred impact.
- Aligns with the broader evidence-first posture used in Incident Timeline.

### Alternatives considered

- Alternative A - Render hard verdicts such as "network policy blocked traffic." Rejected because the app cannot prove that with current inputs.
- Alternative B - Avoid summaries entirely and show raw objects only. Rejected because it leaves too much manual interpretation work.

---

## Decision 003 - Helm preview is read-only and capability-aware

**Status:** Accepted

**Date:** 2026-04-12

### Context

Operators need to see what a Helm change would do, but preview support can vary based on installed CLI tooling and chart availability.

### Decision

Treat Helm preview as read-only. The backend must report whether full diff, degraded preview, or unsupported capability is available, and the UI must show that state explicitly.

### Consequences

- Keeps the operator model safe.
- Avoids opaque failures when the CLI environment differs.
- Requires tests for both supported and unsupported paths.

### Alternatives considered

- Alternative A - Assume the full diff toolchain is always present. Rejected because that will fail unpredictably.
- Alternative B - Skip preview entirely. Rejected because preview is one of the highest-value operator asks.

---

## Decision 004 - Scope the first delivery to namespace and selected-workload context

**Status:** Accepted

**Date:** 2026-04-12

### Context

Cluster-wide diagnostics can become noisy and expensive quickly. The current page already centers on the selected namespace and resource.

### Decision

The first delivery will scope new diagnostics to the active namespace and selected workload or ingress or Helm release, expanding only when a clear use case appears.

### Consequences

- Keeps latency and cognitive load under control.
- Fits the existing selection and panel model.
- Leaves cluster-wide governance or fleet views to later work.

### Alternatives considered

- Alternative A - Start with cluster-wide overviews. Rejected because it broadens the feature and the data volume too early.
- Alternative B - Restrict diagnostics to pods only. Rejected because namespace and Helm context are core parts of operator diagnosis.

---

## Decision 005 - Load Wave 2 diagnostics on demand inside the existing panel column

**Status:** Accepted

**Date:** 2026-04-15

### Context

Ingress and network-policy analysis needs cluster reads that are heavier and more situational than the main browse grids. Folding that data into the page-level cache and refresh loop would increase background load and complicate the existing panel pause behavior.

### Decision

Implement Wave 2 diagnostics as self-loading side panels that fetch analysis only when the operator opens them.

### Consequences

- Keeps the main browse-data cache and periodic refresh path stable.
- Reuses the existing side-panel open state to pause auto-refresh.
- Requires explicit refresh actions inside the diagnostics panels and focused panel-host tests.

### Alternatives considered

- Alternative A - Preload analysis with the main page data. Rejected because most operators will not inspect every workload or ingress and the extra reads would be wasted.
- Alternative B - Create a second diagnostics route. Rejected because it would duplicate the AKS page selection and panel model.
