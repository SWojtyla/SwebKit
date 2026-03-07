# SwebKit — Roadmap & Status Tracker

> Architecture and full design: [DESIGN.md](./DESIGN.md) | [ARCHITECTURE.md](./ARCHITECTURE.md)

## Progress Overview

| Phase | Title | Status | Detail |
|---|---|---|---|
| 1 | Foundation & MVP | 🔄 In Progress | [PHASE1-MVP.md](./phases/PHASE1-MVP.md) |
| 2 | Service Bus Power Features | ⏳ Pending | [PHASE2-SERVICE-BUS.md](./phases/PHASE2-SERVICE-BUS.md) |
| 3 | Observability Depth | ⏳ Pending | [PHASE3-OBSERVABILITY.md](./phases/PHASE3-OBSERVABILITY.md) |
| 4 | AKS Depth | ⏳ Pending | [PHASE4-AKS.md](./phases/PHASE4-AKS.md) |
| 5 | Polish & Advanced | ⏳ Pending | [PHASE5-POLISH.md](./phases/PHASE5-POLISH.md) |

**Status legend:** ✅ Done | 🔄 In Progress | ⏳ Pending | ❌ Blocked

---

## Phase Summaries

### Phase 1 — Foundation & MVP
Working app skeleton. Real connections to Azure Service Bus, App Insights, and AKS.
Basic inspect/query/list for each pillar. Project+Environment model fully working.

### Phase 2 — Service Bus Power Features
DLQ batch operations, message composer, templates & scenarios, auto-refresh, filter presets.

### Phase 3 — Observability Depth
Trace timeline, metrics dashboard (charts), OTLP provider, saved queries, cross-linking.

### Phase 4 — AKS Depth
Live log tailing, port-forward management, embedded terminal (xterm.js), real-time pod watch.

### Phase 5 — Polish & Advanced
Full command palette, reorderable tabs, notifications, import/export config, keyboard audit,
cross-platform testing, performance profiling.
