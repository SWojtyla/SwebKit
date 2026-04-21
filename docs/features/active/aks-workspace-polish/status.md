# Status — aks-workspace-polish

---

title: "Status — aks-workspace-polish"
owner: ""
state: "Complete"
jira: "not linked"
branch: ""
started: "2026-04-20"
last_updated: "2026-04-25"

---

## Quick summary

All 11 items across 4 waves implemented. Build passes. 36 unit tests pass.

**Jira:** not linked

**Current focus:** Ready for pre-ship review.

## Progress checklist

### Wave 1 — Visual signal layer (pure CSS + minimal Razor logic)

- [x] #1 Log line severity colouring (`PodLogView.razor` + `PodLogView.razor.css`)
- [x] #2 Status row tinting for unhealthy pods/deployments (`PodGrid`, `DeploymentGrid`, `StatefulSetGrid`)

### Wave 2 — Interaction improvements (Razor logic, no model changes)

- [x] #3 Events panel: type/kind filter + jump-to-resource link
- [x] #4 Dynamic keyboard hint bar based on selected row state
- [x] #5 CronJob next-run countdown tooltip
- [x] #6 Namespace selector "All namespaces" quick chip

### Wave 3 — Operational features (port-forward UX + model change)

- [x] #10 Port-forward "Open in browser" button
- [x] #11 Pinned port-forward targets (UserSettings model + PortForwardStartDialog)

### Wave 4 — Panel completions

- [x] #13 Wire Helm diff into rollback confirmation flow
- [x] #14 YAML editor structural pre-validation
- [x] #16 Container detail: requests/limits vs actual usage

### Cross-cutting

- [x] Tests updated (`SwebKit.App.Tests` — 36 new tests: log level, cron next-run, HTTP port detection, pinned eviction)
- [ ] Docs aligned (`docs/architecture/design.md` AKS section if behavior changed)
- [ ] Ready for pre-ship review

## Completed

- Wave 1: #1, #2
- Wave 2: #3, #4, #5, #6
- Wave 3: #10, #11
- Wave 4: #13, #14, #16
- Unit tests: 36/36 passing

## Remaining

- Pre-ship review
- Optionally update `docs/architecture/design.md` AKS section

## Blockers

- None.

## Validation

- Test Plan: `test-plan.md`
- Validation status: All unit tests pass (36/36). Build passes with 0 errors.

## Notes

- `FormatCountdown` has no sub-minute resolution; 30s formats as "in 0m". Tests reflect actual implementation.
- `GetLineClass` and `IsHttpPort` promoted from private to internal to allow direct test invocation.
- `Icons.Regular.Size16.Split` does not exist in FluentUI v4.14.0; `SplitHorizontal` was used instead for the Helm diff button.
