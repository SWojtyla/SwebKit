# Feature Overview — Performance Improvements

---

title: "Feature Overview — Performance Improvements"
owner: ""
status: "Planned"
created: "2026-03-24"
updated: "2026-03-24"

---

## Goal

Eliminate perceived UI freezes and blank-page periods in SwebKit by making page navigation, data loading, and app startup feel instant through progressive rendering, async infrastructure, and smart state caching.

## Value

SwebKit currently freezes the entire UI shell during `AppState.InitializeAsync()` at startup and shows blank screens during page transitions while data loads. Users see frozen UI instead of progress indicators, leading to uncertainty about whether the app is working. This feature systematically addresses every layer of the loading pipeline — from app startup through per-page data fetching — so the UI always feels responsive.

## Scope

### In scope

- **PERF-1 to PERF-4**: App startup & MainLayout responsiveness
- **PERF-5 to PERF-6**: Async event bus migration
- **PERF-7 to PERF-12**: Per-page loading optimization (AKS, Service Bus, Pipelines, Redis, Storage)
- **PERF-13 to PERF-16**: Loading UX — skeleton screens, timeout detection, cancel support
- **PERF-17 to PERF-18**: Navigation state caching

### Out of scope

- New features or top-level functionality
- Backend API optimization (Azure SDK call tuning)
- Infrastructure or deployment changes
- Multi-user / cloud sync
- Items already fully scoped in `qol-improvements/` — this feature cross-references but does not duplicate them

## Dependencies

- **QOL Improvements catalog** — items UI-8 through UI-11 (error/loading infrastructure) have direct overlap. See [Cross-reference](#cross-reference-with-qol-improvements) below.
- **Fluent UI Blazor** — skeleton/shimmer components may leverage existing Fluent primitives
- **AppStateService singleton** — changes to init flow affect all pages

## Risks & mitigations

| Risk                                                                                              | Severity  | Mitigation                                                                                                     |
| ------------------------------------------------------------------------------------------------- | --------- | -------------------------------------------------------------------------------------------------------------- |
| Changing `AppState.InitializeAsync` could break assumptions about state availability at page load | 🔴 HIGH   | Phase the migration: make profiles available first, UI state second; add null-safe guards to consumers         |
| Async event bus may introduce subtle ordering bugs                                                | 🟡 MEDIUM | Preserve synchronous handlers as default; only opt-in to async for specific subscribers; add integration tests |
| Skeleton screens may flash briefly on fast connections                                            | 🟢 LOW    | Use minimum display duration (200ms) to prevent flicker                                                        |
| State caching could serve stale data after config changes                                         | 🟡 MEDIUM | Invalidate cache on profile switch and config save events                                                      |

## Cross-reference with QOL improvements

The following items in `docs/features/active/qol-improvements/` overlap with this feature:

| QOL Item                          | Overlap                                                             | Handling                                                                                                        |
| --------------------------------- | ------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| **UI-8** Generic error boundary   | Precondition for reliable error recovery during progressive loading | Referenced as dependency; implementation details live in `qol-improvements/ui-shell.md`                         |
| **UI-9** Skeleton loaders         | Direct overlap — skeleton screen UI design                          | This feature defines the _when_ and _where_; QOL defines the _component design_. PERF-13 cross-references UI-9. |
| **UI-10** Retry with backoff      | Complementary — retry is part of the loading failure path           | Referenced; no duplication                                                                                      |
| **UI-11** Error message expansion | Complementary                                                       | Referenced; no duplication                                                                                      |

**Rule:** Implement UI-8 and UI-9 from the QOL catalog first (or in parallel with PERF Wave 1). This feature assumes they exist and builds on them.

## Related documents

- Architecture: `docs/architecture/architecture.md`
- Pitfalls: `docs/pitfalls/blazor-maui.md` (BL-2, BL-3), `docs/pitfalls/dotnet-csharp.md` (CS-2)
- QOL catalog: `docs/features/active/qol-improvements/index.md`
- QOL UI shell plan: `docs/features/active/qol-improvements/ui-shell.md`

## Quick links

- Status: [status.md](./status.md)
- Frontend plan: [frontend.md](./frontend.md)
- Backend plan: [backend.md](./backend.md)
- Tests: [test-plan.md](./test-plan.md)
- Decisions: [decisions.md](./decisions.md)
