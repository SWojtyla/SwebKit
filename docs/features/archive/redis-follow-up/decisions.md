# Decisions - Redis Follow-up

---

title: "Decisions - Redis Follow-up"
owner: ""
status: "Done"

---

## Decision 001 — Support multiple caches per environment

**Status:** Proposed

**Date:** 2026-03-12

### Context

Single-cache configuration is too restrictive for real developer workflows where multiple Redis instances are used per environment.

### Decision

Evolve Redis configuration to support multiple named caches with explicit active-cache selection.

### Consequences

- Requires backward-compatible model migration.
- Enables cache dropdown UX and clearer cache-context awareness.

### Alternatives considered

- Keep single cache and rely on manual connection string edits — rejected due to UX friction.
- Add global cache list not tied to environment — rejected to preserve environment scoping model.

---

## Decision 002 — Replace `Flush DB` wording with `Purge All`

**Status:** Proposed

**Date:** 2026-03-12

### Context

`Flush DB` is technically accurate but less clear for many users.

### Decision

Use `Purge All` as the primary destructive action label while preserving existing production safety confirmation.

### Consequences

- Clearer user intent in UI.
- Requires consistency across button labels, confirmation dialogs, and tests.

### Alternatives considered

- Keep `Flush DB` terminology — rejected for clarity reasons.

---

## Decision 003 — Remove Redis server info action from primary page

**Status:** Proposed

**Date:** 2026-03-12

### Context

The server info dashboard is not used and adds visual/maintenance overhead.

### Decision

Remove the Server Info button and related panel from Redis v2 scope.

### Consequences

- Simplifies toolbar and focus.
- Requires removal or de-prioritization of server info UI tests and docs references.

### Alternatives considered

- Keep it behind an advanced toggle — rejected to reduce complexity for now.
