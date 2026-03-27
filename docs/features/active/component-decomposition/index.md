# Feature Overview — Component Decomposition

---

title: "Feature Overview — Component Decomposition"
owner: ""
status: "Proposed"
created: "2026-03-27"
updated: "2026-03-27"

---

## Goal

Decompose the three god page components — AksPage (2,415 lines), RedisPage (1,075 lines), and ServiceBusPage (794 lines) — into focused orchestrators backed by extracted sub-components. Each page should own layout and state routing only; domain logic lives in child components.

## Value

- **Maintainability:** Large single-file components slow every future change. Splitting the ~300-line YAML viewer and ~200-line Helm panel out of AksPage means a Helm UX fix no longer requires reading 2,400 lines of context.
- **Testability:** Extracted components can be tested in isolation with bUnit. Currently, testing YAML edit mode or Helm rollback requires rendering the entire AksPage — expensive, fragile, and slow.
- **Parallelism:** Separate files enable concurrent work on different concerns (e.g., YAML viewer UX vs. connection bar improvements).
- **Reduced merge conflicts:** One of the biggest churn areas — AksPage — becomes multiple small files.

## Scope

**In scope:**

- Phase 1 — AksPage decomposition (critical): extract AksYamlViewer, AksHelmPanel, AksResourceActions, AksConnectionBar → reduce AksPage from ~2,415 to <300 lines
- Phase 2 — RedisPage decomposition (moderate): extract RedisConnectionBar, RedisToolbar → reduce from ~1,075 to <400 lines
- Phase 3 — ServiceBusPage cleanup (low priority): extract ServiceBusNamespacePanel → reduce from ~794 to <500 lines

**Out of scope:**

- No behavioral changes — every user-visible feature works identically before and after
- No new features, no new API surface
- No backend/service changes (all work is in `SwebKit.App`)
- No touching already-extracted sub-components (32+ Aks sub-components, 6 ServiceBus components, 5 Redis components already exist)
- FQ-2 (AppStateService decomposition) — dropped; see `decisions.md` D-002

## Dependencies

- `frontend-quality` feature (waves 0-4 complete) — provides `SwebKitComponentBase`, cleaned event subscriptions, and CSS isolation groundwork
- `performance-v2` feature (done) — provides InvokeAsync migration, CTS patterns, render batching that this work must preserve
- Pitfalls BL-1 through BL-6, CS-2 — apply to all new components

## Risks & mitigations

| Risk                                                            | Impact                                                      | Mitigation                                                                                                                           |
| --------------------------------------------------------------- | ----------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| Regression in YAML view/edit after extraction                   | High — YAML editing is used in production AKS management    | Extract with exact method signatures; add dedicated bUnit tests for AksYamlViewer covering view, edit, search, save, cancel flows    |
| Event wiring bugs when splitting EventBus subscriptions         | Medium — missed Dispose can leak subscriptions              | Each new component that subscribes must implement IDisposable; test Dispose path in bUnit                                            |
| Context menu z-index/positioning breaks after DOM restructuring | Medium — context menus use absolute positioning             | Keep context menu markup at the same DOM nesting level (page root) during Phase 1; move to component scope only in Phase 2 if stable |
| Parameter cascade breaks state propagation                      | Medium — data-down model requires correct parameter binding | Add render-count assertions in bUnit tests to catch unnecessary re-renders (BL-5)                                                    |
| Helm rollback confirmation flow breaks during extraction        | High — rollback in prod is destructive                      | Keep the AksConfirmBar reference in AksPage; pass it to AksHelmPanel via `[Parameter]`                                               |

## Related documents

- Architecture: [architecture.md](../../architecture/architecture.md)
- Design: [design.md](../../architecture/design.md)
- Pitfalls — Blazor/MAUI: [blazor-maui.md](../../pitfalls/blazor-maui.md)
- Pitfalls — .NET/C#: [dotnet-csharp.md](../../pitfalls/dotnet-csharp.md)
- Predecessor: [frontend-quality](../frontend-quality/index.md) (FQ-1 deferred from there)

## Quick links

- Status: [status.md](status.md)
- Frontend plan: [frontend.md](frontend.md)
- Decisions: [decisions.md](decisions.md)
- Tests: [test-plan.md](test-plan.md)
