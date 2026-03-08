# Decisions - Service Bus

---

title: "Decisions - Service Bus"
owner: ""
status: ""

---

## Decision 001 — Namespace and connection-string handling

**Status:** Accepted

**Date:** 2026-03-08

### Context

Feature needs a simple global registry for Service Bus namespaces that is resilient across restarts and easy for developers to add via connection string.

### Decision

Store global namespaces in `ProfileData.ServiceBusNamespaces` and prefer a primary `AzureServiceBusClient(string connectionString)` constructor to simplify usage. Keep legacy constructors for compatibility.

### Consequences

- Simple add-by-connection-string UX.
- Credentials may be stored in credential manager keyed by `sb:ns:{guid}`.
- Project-scoped pins are stored separately in `ProjectEnvironment.ServiceBusEntityLinks`.

### Alternatives considered

- Per-project namespace storage — rejected for complexity and cross-project reuse.

---

## Decision 002 — Explicit Active vs DLQ selection in EntityTree

**Status:** Accepted

**Date:** 2026-03-08

### Context

DLQ counts and active counts were ambiguous; users needed explicit mode selection.

### Decision

Add explicit `Active` and `DLQ` actions on entity rows and surface both counts simultaneously. Default row click remains Active for quick access.

### Consequences

- Clearer UX for DLQ vs Active operations.
- Requires mode-aware tab IDs and DLQ labeling.

---
