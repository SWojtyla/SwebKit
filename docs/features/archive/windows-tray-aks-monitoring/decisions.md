# Decisions - windows-tray-aks-monitoring

---

title: "Decisions - windows-tray-aks-monitoring"
owner: "GitHub Copilot"
created: "2026-03-31"
updated: "2026-04-10"

---

## Decision Log

### Decision 001 - Scope tray behavior to Windows only

**Date:** 2026-03-31
**Status:** Accepted

**Context:**
The requested behavior was explicit for Windows system tray while preserving AKS monitoring continuity.

**Decision:**
Implement tray lifecycle behavior only for `net10.0-windows`. Keep non-Windows targets on existing behavior.

**Rationale:**
Delivers requested behavior with lower risk and avoids cross-platform abstraction work that was not required.

**Consequences:**

- Faster delivery with smaller regression surface.
- Cross-platform tray support remains a separate follow-up if needed.

### Decision 002 - Minimize and Close both hide to tray; explicit Exit required

**Date:** 2026-03-31
**Status:** Accepted

**Context:**
Background AKS monitoring should continue when users dismiss the window, and accidental exits should be reduced.

**Decision:**
Intercept both Minimize and Close to hide the window to tray. Provide explicit tray Exit to terminate the process.

**Rationale:**
Matches requested UX and preserves long-running monitoring continuity.

**Consequences:**

- User model changes: window close no longer means full app termination.
- Exit path must preserve existing process cleanup behavior.

### Decision 003 - Reuse existing PodHealthMonitorService as source of truth

**Date:** 2026-03-31
**Status:** Accepted

**Context:**
The app already had a singleton monitor service with persisted state and event dispatch.

**Decision:**
Do not create a tray-specific monitor loop. Subscribe tray indicator behavior to existing monitor events.

**Rationale:**
Avoids duplicate polling, race conditions, and split monitor state.

**Consequences:**

- Tray behavior remains a lifecycle/UI concern, not monitoring-engine ownership.
- Existing monitoring persistence and semantics remain unchanged.
