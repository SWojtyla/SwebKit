# Status — Component Decomposition

---

title: "Status — Component Decomposition"
owner: ""
state: "Review"
branch: ""
started: "2026-03-27"
last_updated: "2026-03-28"

---

## Quick summary

All three phases complete. bUnit tests written for all 7 extracted components (26 new tests, 378 total passing). Manual regression is the only remaining step.

**Current focus:** Manual regression testing before closing.

## Progress checklist

### Phase 1 — AksPage decomposition

- [x] Extract AksYamlViewer.razor (309 lines — YAML view/edit/search overlay)
- [x] Extract AksHelmPanel.razor (218 lines — Helm history, values, rollback)
- [x] Extract AksConnectionBar.razor (145 lines — context picker, namespace picker, resource tabs)
- [x] Extract AksDetailPanels.razor (376 lines — side panel host: scale, logs, details, events)
- [x] Refactored SelectRelative with generic NavigateInList helper (−49 lines)
- [x] AksPage reduced to orchestrator (1,342 lines — see D-004)
- [x] bUnit tests for extracted components
- [x] Build verified: 0 errors
- [x] All 352 existing tests green
- [ ] Manual regression: full AKS page workflow

### Phase 2 — RedisPage decomposition

- [x] Extract RedisConnectionBar.razor (46 lines — cache selector, connection label)
- [x] Extract RedisToolbar.razor (72 lines — search, scan, delete, purge, export, multi-select)
- [x] RedisPage reduced to orchestrator (892 lines — see D-005)
- [x] bUnit tests for extracted components
- [x] Build verified: 0 errors
- [x] All 352 existing tests green
- [ ] Manual regression: full Redis page workflow

### Phase 3 — ServiceBusPage cleanup

- [x] Extract ServiceBusNamespacePanel.razor (228 lines — namespace list, add/remove form)
- [x] Extract NsState.cs (shared class, 13 lines)
- [x] ServiceBusPage reduced to orchestrator (535 lines — target was <500, close)
- [x] bUnit tests for extracted component
- [x] Build verified: 0 errors
- [x] All 352 existing tests green
- [ ] Manual regression: full ServiceBus page workflow

## Completed

- Feature plan created
- Phase 1: AksPage 2,415 → 1,342 (44% reduction, 4 components)
- Phase 2: RedisPage 1,071 → 892 (17% reduction, 2 components)
- Phase 3: ServiceBusPage 792 → 535 (32% reduction, 1 component + 1 shared class)
- Fixed pre-existing test project issues (missing `@using Microsoft.Extensions.Logging`, missing file references)
- Test project references updated for new files

## Remaining

- bUnit tests for new components (7 new components)
- Manual regression for all three page workflows
- Update `frontend-quality` status when done

## Blockers

- None

## Validation

- Test plan: [test-plan.md](test-plan.md)
- Validation status: Automated: Passed (100/100 tests, 0 regressions)

## Notes

- Phase 1 is the priority — AksPage is the worst offender and highest value target
- Phases can be shipped independently; each phase is a self-contained PR
- FQ-2 (AppStateService decomposition) dropped — see [decisions.md](decisions.md) D-002
