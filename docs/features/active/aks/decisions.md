# Decisions - aks

---

title: "Decisions - aks"
owner: ""
status: "In Progress"

---

## Decision 001 — Read from kubeconfig as source of truth

**Status:** Accepted

**Date:** 2026-03-08

### Context

Users need fast access to cluster resources without separately configuring cluster endpoints in app-specific forms.

### Decision

Use kubeconfig files as the primary configuration source, supporting both default kubeconfig path and explicit file selection.

### Consequences

- Aligns behavior with existing Kubernetes tooling (`kubectl`, IDE plugins).
- Requires robust parsing and clear error feedback for invalid/expired kubeconfig entries.

### Alternatives considered

- App-specific AKS credential forms — rejected due to duplicated configuration and higher support burden.

---

## Decision 002 — Namespace-first browsing model

**Status:** Accepted

**Date:** 2026-03-08

### Context

Most AKS troubleshooting and workload inspection is namespace-scoped.

### Decision

Require explicit namespace selection and scope all resource lists (pods, deployments, helm releases, ingresses) to the active namespace.

### Consequences

- Reduces noise in large clusters.
- Requires clear indication of current namespace and simple switching controls.

### Alternatives considered

- Cluster-wide lists by default — rejected because of high noise and performance cost.

---

## Decision 003 — Read-only YAML inspection in this phase

**Status:** Accepted

**Date:** 2026-03-08

### Context

The current feature objective is safe inspection and troubleshooting, not mutation.

### Decision

Expose read-only YAML viewing for supported resource kinds; defer edit/apply/delete operations.

### Consequences

- Safer first release with lower accidental-change risk.
- Leaves room for a future controlled edit/apply feature with stronger validation and confirmations.

### Alternatives considered

- Full inline YAML editor with apply — rejected for initial scope and safety concerns.
