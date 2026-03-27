# Status — Component Decomposition

---

title: "Status — Component Decomposition"
owner: ""
state: "Proposed"
branch: ""
started: ""
last_updated: "2026-03-27"

---

## Quick summary

Feature planned. Three phases defined. No implementation started yet.

**Current focus:** Review plan and approve Phase 1 scope before implementation begins.

## Progress checklist

### Phase 1 — AksPage decomposition

- [ ] Extract AksYamlViewer.razor (YAML view/edit/search overlay)
- [ ] Extract AksHelmPanel.razor (Helm history, values, rollback)
- [ ] Extract AksResourceActions service (context menu action handlers)
- [ ] Extract AksConnectionBar.razor (context picker, namespace picker, connection status)
- [ ] AksPage reduced to orchestrator (<300 lines)
- [ ] bUnit tests for all extracted components
- [ ] Manual regression: full AKS page workflow

### Phase 2 — RedisPage decomposition

- [ ] Extract RedisConnectionBar.razor (cache selector, database picker)
- [ ] Extract RedisToolbar.razor (action buttons, multi-select controls)
- [ ] RedisPage reduced to orchestrator (<400 lines)
- [ ] bUnit tests for extracted components
- [ ] Manual regression: full Redis page workflow

### Phase 3 — ServiceBusPage cleanup

- [ ] Extract ServiceBusNamespacePanel.razor (namespace list, add form)
- [ ] ServiceBusPage reduced to orchestrator (<500 lines)
- [ ] bUnit tests for extracted component
- [ ] Manual regression: full ServiceBus page workflow

## Completed

- Feature plan created

## Remaining

- All implementation (Phases 1-3)
- All testing
- Update `frontend-quality` status to reflect FQ-1 completion when done

## Blockers

- None

## Validation

- Test plan: [test-plan.md](test-plan.md)
- Validation status: Not started

## Notes

- Phase 1 is the priority — AksPage is the worst offender and highest value target
- Phases can be shipped independently; each phase is a self-contained PR
- FQ-2 (AppStateService decomposition) dropped — see [decisions.md](decisions.md) D-002
