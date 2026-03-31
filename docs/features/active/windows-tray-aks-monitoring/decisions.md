# Decisions - windows-tray-aks-monitoring

---

title: "Decisions - windows-tray-aks-monitoring"
owner: "GitHub Copilot"
status: "Active"

---

## Decision 001 - Scope tray behavior to Windows only

**Status:** Accepted

**Date:** 2026-03-31

### Context

The requested behavior is explicit: minimize to Windows system tray while monitoring AKS namespaces in background. Current project target usage is Windows desktop for MAUI Hybrid operations.

### Decision

Implement tray lifecycle behavior only for Windows (`net10.0-windows`) and do not introduce cross-platform abstractions for non-Windows trays in this feature.

### Consequences

- Faster delivery with lower risk.
- No impact to non-Windows runtime targets.
- If cross-platform tray support is needed later, it will be a follow-up feature.

### Alternatives considered

- Cross-platform tray abstraction now - rejected due added scope and no current requirement.
- Keep existing close behavior and add only minimize handling - rejected because user requested both Minimize and Close routes.

---

## Decision 002 - Minimize and Close both hide to tray; explicit Exit is required

**Status:** Accepted

**Date:** 2026-03-31

### Context

Accidental app closure currently breaks long-running monitoring visibility. Requested behavior requires app presence in tray on both Minimize and Close actions.

### Decision

Intercept both window Minimize and Close actions to hide to tray. Add explicit Exit action in tray menu to perform full process termination.

### Consequences

- Monitoring continuity improves for long-running AKS observation.
- User mental model must be clear that Close is now hide-to-tray, not full exit.
- Explicit exit path must preserve existing process shutdown cleanup.

### Alternatives considered

- Minimize only routes to tray, Close exits - rejected because it does not satisfy requested behavior.
- Add prompt on every close - rejected as noisy and unnecessary for regular workflow.

---

## Decision 003 - Reuse existing PodHealthMonitorService as monitoring source of truth

**Status:** Accepted

**Date:** 2026-03-31

### Context

The app already has a singleton `PodHealthMonitorService` with persisted namespace list and monitoring enabled state, plus event dispatch and toast integration.

### Decision

Do not create a second monitoring loop. Tray indicator behavior will subscribe to existing pod health events and reflect monitor state from current services/config.

### Consequences

- Avoid duplicate polling, race conditions, and inconsistent alert state.
- Lower implementation risk and smaller regression surface.
- Tray behavior remains a lifecycle/UI addition, not a monitoring engine rewrite.

### Alternatives considered

- New tray-specific monitoring service - rejected due duplication and synchronization risk.
- Poll only while AKS page is open - rejected because background continuity is a core requirement.
