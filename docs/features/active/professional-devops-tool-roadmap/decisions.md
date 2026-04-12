# Decisions - professional-devops-tool-roadmap

---

title: "Decisions - professional-devops-tool-roadmap"
owner: "GitHub Copilot"
status: "Planned"

---

## Decision 001 - Prioritize shell UX before deeper feature work

**Status:** Accepted

**Date:** 2026-04-12

### Context

SwebKit already has multiple operator areas, but the shell experience remains inconsistent across navigation, page headers, empty states, refresh language, and safety cues.

### Decision

The next delivery wave starts with shell UX consistency before additional domain-depth or cross-workflow enhancements.

### Consequences

- Later features can reuse one shell language instead of inventing their own.
- Some domain-specific asks are intentionally deferred until shell quality is high enough to support them.

### Alternatives considered

- Alternative A - start with another domain feature first: rejected because it would duplicate shell polish and create more UI drift.

---

## Decision 002 - Keep the roadmap as plan-of-plans only

**Status:** Accepted

**Date:** 2026-04-12

### Context

Roadmap documents tend to accumulate implementation notes and become unmaintainable umbrella specs.

### Decision

This feature owns sequencing, dependencies, and wave governance only. Every concrete slice still needs its own active feature folder and implementation modules.

### Consequences

- The roadmap stays durable and easy to audit.
- Downstream features remain the authoritative source of truth for scope and technical design.

### Alternatives considered

- Alternative A - keep all future work inside one roadmap folder: rejected because it would collapse multiple scopes into one giant feature.

---

## Decision 003 - Treat incident-timeline-workbench as a separate dependency, not roadmap scope

**Status:** Accepted

**Date:** 2026-04-12

### Context

`incident-timeline-workbench` is already an active feature with its own goal, status, decisions, and test plan.

### Decision

The roadmap references the incident workbench only as the base for later incident workflow expansion. It does not modify or absorb that feature.

### Consequences

- Incident planning remains traceable in its own folder.
- Wave 4 follow-ons can be split cleanly instead of being forced into one active feature.

### Alternatives considered

- Alternative A - rewrite the incident feature under the roadmap folder: rejected because it would blur ownership and history.

---

## Decision 004 - Split wave-5 domain depth into separate feature folders

**Status:** Accepted

**Date:** 2026-04-12

### Context

Service Bus, AKS, Observability, Pipelines, Redis, and Storage each have different operator workflows, risks, and validation needs.

### Decision

Any wave-5 domain-depth work must be created as a dedicated active feature folder per capability area rather than as one domain-depth umbrella item.

### Consequences

- Scope stays implementation-sized.
- Validation and decisions remain capability-specific.
- The roadmap can evolve candidate ordering without rewriting detailed plans in one large file.

### Alternatives considered

- Alternative A - one domain-depth feature for all areas: rejected because it would be too broad to execute cleanly.
