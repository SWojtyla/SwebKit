# Status - Pipeline Groups

---

title: "Status - Pipeline Groups"
owner: ""
state: "Proposed"
jira: ""
branch: ""
started: ""
last_updated: "2026-04-18"

---

## Quick summary

Implementation complete. All components built, integrated, and compiling cleanly.

**Jira:** not linked

**Current focus:** Ready for manual testing and review.

## Progress checklist

- [x] Planning complete
- [x] Domain model (`PipelineGroup` + `PipelineGroupEntry`) added to `DevOpsConfig`
- [x] `PipelineGroups` property added — serialised with existing `ProfileRepository` JSON save
- [x] Groups tab added to `PipelinesPage`
- [x] Group list component (`PipelineGroupList.razor`)
- [x] Group editor panel (`PipelineGroupEditor.razor`) — add/remove pipelines, stale warning, rename on blur
- [x] Group trigger dialog (`PipelineGroupTriggerDialog.razor`) — per-pipeline branch select, sequential Run All, real-time status
- [x] Per-pipeline trigger feedback (success/error badges)
- [x] Stale pipeline warning badge
- [x] Build passes with no new errors
- [ ] Unit tests for new domain model
- [ ] Docs aligned (codebase-guide, functionalities if applicable)
- [ ] Manual QA: create, edit, delete, trigger group

## Progress checklist

- [x] Planning complete
- [ ] Domain model (`PipelineGroup` + `PipelineGroupEntry`) added to `DevOpsConfig`
- [ ] `ProfileRepository` serialisation covers `PipelineGroups`
- [ ] Groups tab added to `PipelinesPage`
- [ ] Group list component (`PipelineGroupList.razor`)
- [ ] Group editor panel (`PipelineGroupEditor.razor`) — add/remove pipelines
- [ ] Group trigger dialog (`PipelineGroupTriggerDialog.razor`) — branch select + Run All
- [ ] Per-pipeline trigger feedback (success/error)
- [ ] Stale pipeline warning badge
- [ ] Unit tests for new domain model + any service logic
- [ ] Docs aligned (codebase-guide, functionalities if applicable)
- [ ] Ready for review

## Completed

_(nothing yet)_

## Remaining

- All of the above

## Blockers

None.

## Validation status

Not started.
