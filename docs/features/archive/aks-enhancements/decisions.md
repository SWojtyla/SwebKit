# Decisions - AKS Enhancements

---

title: "Decisions - AKS Enhancements"
owner: ""
status: "Planned"

---

## Decision 002 — Simplify AKS settings and make Azure auth automatic

**Status:** Accepted

**Date:** 2026-03-11

### Context

The AKS settings form exposed fields that added complexity without value: Explicit Cluster URL (unused), Use Azure Credential Fallback checkbox, and CredentialRef. The save action had no visible feedback, making it unclear whether settings were persisted. Authentication should be automatic based on kubeconfig content.

### Decision

- Remove `ExplicitClusterUrl`, `UseAzureCredentialFallback`, and `CredentialRef` from `AksConfig`.
- Always apply Azure credential fallback automatically — the existing `ShouldUseAzureCredentialFallback` logic already gates activation to AKS hosts with missing tokens, so no user toggle is needed.
- Simplify the settings form to three optional fields: Kubeconfig Path, Default Context, Default Namespace.
- Add inline "Saved" feedback and a current-config summary to the form so users can see what's persisted.

### Consequences

- Fewer settings to confuse users; authentication just works from kubeconfig.
- Breaking change to `AksConfig` serialization (removed properties are ignored on deserialization).
- `KubernetesAksClient` constructor simplified to two parameters.

### Alternatives considered

- Keep the Azure fallback toggle as an advanced option — rejected because the auto-detection logic is reliable and the toggle adds no practical value.

---

## Decision 001 — Follow-up after connectivity foundation archive

**Status:** Accepted

**Date:** 2026-03-10

### Context

AKS connectivity and credential compatibility were delivered and archived, but the full resource-browser scope remains.

### Decision

Create a dedicated follow-up feature to complete context discovery, namespace/resource browsing, and YAML inspection without reopening archived execution tracking.

### Consequences

- Active execution remains focused and truthful.
- Archive remains concise and usable as historical reference.
