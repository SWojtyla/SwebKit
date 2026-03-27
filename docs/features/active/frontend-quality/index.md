# Feature Overview — Frontend Code Quality & Architecture Hardening

---

title: "Feature Overview — Frontend Code Quality & Architecture Hardening"
owner: ""
status: "Proposed"
created: "2026-03-27"
updated: "2026-03-27"

---

## Goal

Harden the SwebKit MAUI Blazor Hybrid frontend by eliminating memory leaks, decomposing god components, consolidating duplicated patterns, improving UX consistency, and cleaning up CSS. The result is a codebase that is easier to extend, safer in long sessions, and more consistent for users.

## Value

- **Memory safety:** Event subscription leaks and unbounded singleton growth cause degradation in long-running sessions. Fixing these prevents silent resource exhaustion.
- **Maintainability:** God components (700+ line pages) and duplicated load/modal/toolbar patterns slow every future change. Decomposition and DRY extraction cut the cost of new features.
- **UX polish:** Inconsistent error surfaces, missing ARIA labels, underutilized skeleton states, and lost tab state reduce user trust. Standardizing these creates a more professional tool.
- **CSS hygiene:** Inline styles (~80%) and inconsistent CSS isolation make theming and maintenance harder.

## Scope

**In scope — 21 items across 5 waves:**

| Wave | Theme                        | Items                                           |
| ---- | ---------------------------- | ----------------------------------------------- |
| 0    | Safety & Memory              | FQ-3, FQ-4, FQ-5                                |
| 1    | Architecture & Decomposition | FQ-1, FQ-2, FQ-10, FQ-13                        |
| 2    | Performance Polish           | FQ-6, FQ-7, FQ-8, FQ-9                          |
| 3    | UX Consistency & Polish      | FQ-11, FQ-12, FQ-14, FQ-15, FQ-16, FQ-17, FQ-18 |
| 4    | CSS & Style Cleanup          | FQ-19, FQ-20, FQ-21                             |

**Out of scope:**

- Items already addressed by `performance-v2`: async void fixes, bare StateHasChanged → InvokeAsync migration, CTS races (Interlocked swap), render batching, AKS log virtualization, `@key` directives
- Backend API changes (all changes are in `SwebKit.App` only)
- Infrastructure / deployment changes
- New NuGet dependencies (unless justified in a decision record)
- New features or functional behavior changes

## Dependencies

- `performance-v2` must be fully merged before starting (avoids conflicts in the same files) — **Status: Done**
- Existing shared components: `DataTable`, `FilterBar`, `Modal`, `ConfirmDialog`, `ErrorCallout`, `SkeletonRows`, `LoadingSpinner`
- Fluent UI Blazor component library (already in use)
- bUnit test framework (already in use)
- Pitfalls: BL-1 through BL-7, CS-1, CS-2

## Risks & mitigations

- **Risk:** God component decomposition (FQ-1) changes prop flow and event wiring across ServiceBusPage, AksPage, RedisPage — large surface for regressions — **Mitigation:** Decompose one page at a time with full bUnit test coverage before and after; use extract-component refactoring (preserve public API, then simplify)
- **Risk:** AppStateService decomposition (FQ-2) is used as CascadingValue everywhere — **Mitigation:** Facade pattern preserves backward compatibility; new focused services are injected only in new code, old code migrates incrementally
- **Risk:** EventBus API change (FQ-4) breaks all existing subscribers — **Mitigation:** Add `IDisposable` return overload alongside existing void API; migrate callers wave by wave
- **Risk:** Inline style extraction (FQ-19) could subtly change rendering — **Mitigation:** Visual regression checks via manual comparison; extract file-by-file with component test validation

## Related documents

- Architecture: [architecture.md](../../architecture/architecture.md)
- Design: [design.md](../../architecture/design.md)
- Pitfalls — Blazor/MAUI: [blazor-maui.md](../../pitfalls/blazor-maui.md)
- Pitfalls — .NET/C#: [dotnet-csharp.md](../../pitfalls/dotnet-csharp.md)
- Predecessor: [performance-v2](../performance-v2/index.md) (Done)

## Quick links

- Status: [status.md](status.md)
- Frontend plan: [frontend.md](frontend.md)
- Decisions: [decisions.md](decisions.md)
- Tests: [test-plan.md](test-plan.md)
