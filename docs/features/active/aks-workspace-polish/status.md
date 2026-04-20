# Status — aks-workspace-polish

---

title: "Status — aks-workspace-polish"
owner: ""
state: "Planned"
jira: "not linked"
branch: ""
started: "2026-04-20"
last_updated: "2026-04-20"

---

## Quick summary

Plan created. No implementation started. Begin with the pure-CSS items (#1, #2) to establish the visual patterns, then proceed to the UI logic items.

**Jira:** not linked

**Current focus:** Planning complete — ready for implementation to begin.

## Progress checklist

### Wave 1 — Visual signal layer (pure CSS + minimal Razor logic)

- [ ] #1 Log line severity colouring (`PodLogView.razor` + `PodLogView.razor.css`)
- [ ] #2 Status row tinting for unhealthy pods/deployments (`PodGrid`, `DeploymentGrid`, `StatefulSetGrid`)

### Wave 2 — Interaction improvements (Razor logic, no model changes)

- [ ] #3 Events panel: type/kind filter + jump-to-resource link
- [ ] #4 Dynamic keyboard hint bar based on selected row state
- [ ] #5 CronJob next-run countdown tooltip
- [ ] #6 Namespace selector "All namespaces" quick chip

### Wave 3 — Operational features (port-forward UX + model change)

- [ ] #10 Port-forward "Open in browser" button
- [ ] #11 Pinned port-forward targets (UserSettings model + PortForwardStartDialog)

### Wave 4 — Panel completions

- [ ] #13 Wire Helm diff into rollback confirmation flow
- [ ] #14 YAML editor structural pre-validation
- [ ] #16 Container detail: requests/limits vs actual usage

### Cross-cutting

- [ ] Tests updated (`SwebKit.App.Tests`, `SwebKit.Kubernetes.Tests`)
- [ ] Docs aligned (`docs/architecture/design.md` AKS section if behavior changed)
- [ ] Ready for pre-ship review

## Completed

_(none yet)_

## Remaining

All items — see checklist above.

## Blockers

- None at this time.
- Item #13 (Helm diff) depends on `helm-diff` plugin being available in the test environment. Already installed locally (see terminal history). Needs a graceful fallback for machines without the plugin.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- Wave 1 is safe to implement and review independently — no risk to existing behaviour.
- Item #11 touches `UserSettings` domain model; follow CS-4 (atomic JSON write) when saving.
- Items #3 and #4 use already-loaded in-memory data — no new API calls needed.
