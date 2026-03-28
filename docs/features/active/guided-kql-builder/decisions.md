# Decisions - guided-kql-builder

---

title: "Decisions - guided-kql-builder"
owner: ""
status: "Planned"

---

## Decision 001 - Treat guided builder as one-way compile to KQL

**Status:** Accepted

**Date:** 2026-03-28

### Context

Guided mode aims to lower the barrier for non-KQL users, while advanced users need direct KQL edits. Full bidirectional conversion from arbitrary KQL text back into structured builder controls introduces parser complexity, ambiguous mappings, and high maintenance cost.

### Decision

The builder compiles structured input to KQL as a one-way transformation. Switching to advanced mode always carries forward generated KQL, but reverse mapping from arbitrary advanced text back to guided controls is not guaranteed.

### Consequences

- Enables fast delivery of useful guided functionality with predictable behavior.
- Reduces parser complexity and avoids fragile reverse-engineering logic.
- Requires explicit UX messaging when user-edited advanced text cannot be represented in guided mode.

### Alternatives considered

- Alternative A: Full KQL parser with AST to rehydrate guided controls - rejected due complexity and low short-term value.
- Alternative B: Restrict advanced editor to builder-generated text only - rejected because it blocks expert workflows.

---

## Decision 002 - Keep compiler implementation in SwebKit.Observability and contracts in SwebKit.Core

**Status:** Accepted

**Date:** 2026-03-28

### Context

Architecture boundaries require UI logic in `SwebKit.App`, shared contracts in `SwebKit.Core`, and provider-specific query behavior in `SwebKit.Observability`.

### Decision

Define guided query and validation contracts in `SwebKit.Core`, while implementing KQL compilation logic in `SwebKit.Observability` near existing App Insights query execution code.

### Consequences

- Maintains clean separation of concerns across existing project split.
- Keeps provider semantics close to query execution and preset logic.
- Allows unit testing compiler logic without UI dependencies.

### Alternatives considered

- Alternative A: Put compiler in `SwebKit.App` - rejected due UI coupling and reduced testability.
- Alternative B: Put all compiler code in `SwebKit.Core` - rejected because KQL/provider-specific behavior belongs with Observability implementation.

---

## Decision 003 - Preserve existing raw KQL execution path as default compatibility anchor

**Status:** Accepted

**Date:** 2026-03-28

### Context

Current Observability Logs users already rely on direct KQL and saved queries. The new builder must reduce onboarding friction without destabilizing existing workflows.

### Decision

Do not replace current raw KQL execution path. Guided mode augments the Logs experience, and advanced mode remains fully available with no query-capability downgrade.

### Consequences

- Minimizes regression risk for current users.
- Allows incremental adoption of guided mode.
- Requires stronger test coverage for both guided and advanced paths.

### Alternatives considered

- Alternative A: Replace advanced editor entirely with guided mode - rejected due power-user impact.
- Alternative B: Ship guided mode as a separate page - rejected due fragmented user experience and duplicated state handling.
