# Frontend Plan - pipelines-deployment-assurance

---

title: "Frontend Plan - pipelines-deployment-assurance"
owner: "GitHub Copilot"
status: "Not started"

---

## Goal

Deepen the existing Pipelines/Releases hub so operators can see approval urgency, run failure meaning, runtime drift, and validation outcome inline where they already inspect pipelines and releases.

## Impacted areas

- Existing pages and components:
- `src/SwebKit.App/Components/Pages/PipelinesPage.razor`
- `src/SwebKit.App/Components/Pipelines/PipelineDetail.razor`
- `src/SwebKit.App/Components/Pipelines/PipelineActivity.razor`
- `src/SwebKit.App/Components/Pipelines/PipelinesOverview.razor`
- `src/SwebKit.App/Components/Releases/ApprovalCenter.razor`
- `src/SwebKit.App/Components/Releases/ReleaseDetail.razor`
- `src/SwebKit.App/Components/Releases/ReleaseEditor.razor`
- `src/SwebKit.App/Components/Releases/ComponentScopeEditor.razor`
- Likely new UI helpers or page-local components:
- `src/SwebKit.App/Components/Pipelines/DeploymentAssuranceSummary.razor`
- `src/SwebKit.App/Components/Releases/RuntimeBindingEditor.razor`
- `src/SwebKit.App/Components/Releases/ValidationHistoryPanel.razor`
- Likely impacted tests:
- new or expanded `tests/SwebKit.App.Tests/ApprovalCenterTests.cs`
- new or expanded `tests/SwebKit.App.Tests/PipelineDetailTests.cs`
- new or expanded `tests/SwebKit.App.Tests/ReleaseDetailAssuranceTests.cs`

## UX notes

- This feature should stay inside the current `/pipelines` route. It is an assurance layer, not a new navigation area.
- New signals should attach to existing places where operators already look:
- `Approvals` tab for age and SLA state.
- `Pipelines` detail and `Activity` tab for failure classification and validation outcome.
- `Releases` detail for runtime drift, target-versus-runtime version visibility, and validation history.

### User flows

- Approval urgency:
- Open `Approvals`, see age badges and SLA state, filter to breached items, then continue to approve or reject using the current flow.
- Failure triage:
- Open a failed pipeline run and immediately see whether it failed in build/test, gate, deploy, or post-deploy validation.
- Drift inspection:
- Open a release record and compare target tag, last deployment snapshot, and observed runtime version for each component/environment row.
- Manual validation:
- Select a deployment-capable run or release component, click `Validate deployment`, and review the stored outcome with AKS and Observability source coverage.

### Component states

- Loading: assurance details load lazily and should not block the entire Pipelines page.
- Loaded: age or drift or validation signals render with concise text plus badges.
- Partial: validation or drift could only query AKS or only query Observability.
- Unknown: runtime binding or source configuration missing.
- Error: scoped to the affected assurance surface, not the whole page.

### Safety and confirmation

- Existing approval safety stays intact. Production approvals still require typed `CONFIRM` in `ApprovalCenter.razor`.
- New validation actions are read-only and do not require typed confirmation, but they must clearly state that the action only inspects runtime health and does not mutate Azure DevOps, AKS, or the workload.
- Drift or failed validation badges must not imply that SwebKit already rolled anything back or blocked anything automatically.

### Accessibility

- Age, drift, and validation statuses need text labels in addition to color.
- Release-detail tables should remain readable when new assurance columns are added.
- Validation history rows should expose source coverage and outcome text without hover-only information.

## API / contract changes

- The UI should consume already-shaped assurance DTOs from Core services rather than deriving failure categories or drift heuristics in Razor.
- `ComponentScopeEditor` is the likely authoring entry point for runtime binding metadata, because it already owns per-component scope configuration.
- `ReleaseDetail` should remain the canonical view for release-level assurance history.

## Tasks

### Wave 1 - Approval aging and failure surfacing [blazor-expert]

- [ ] Add age and SLA badges to `ApprovalCenter.razor` plus filter or sort affordances for breached items.
- [ ] Add failure classification badges to `PipelineDetail.razor` and `PipelineActivity.razor`.
- [ ] Add lightweight summary counters to `PipelinesOverview.razor` where they help selection.
- [ ] Add focused bUnit coverage for loading, empty, and stale-refresh behavior.

### Wave 2 - Drift state and binding authoring [blazor-expert]

- [ ] Extend `ComponentScopeEditor.razor` with runtime binding fields.
- [ ] Add drift columns or summary cards to `ReleaseDetail.razor` and selected pipeline views.
- [ ] Surface `Matched`, `Drifted`, `Unknown`, and `Not configured` with explanatory copy.
- [ ] Ensure the UI does not crowd the existing release matrix beyond readable limits.

### Wave 3 - Validation loop and history [blazor-expert]

- [ ] Add a `Validate deployment` action at the selected pipeline or release scope.
- [ ] Show in-progress, passed, warning, failed, and partial validation states.
- [ ] Add a small validation history surface that reads persisted snapshots from `ReleaseRepository`.
- [ ] Add tests for cancellation, partial-source rendering, and persisted history reload behavior.

## Validation

- Component tests: Not started
- Manual UX checks:
- Verify production approvals still require typed confirmation after age badges are introduced.
- Verify validation actions communicate their read-only nature.
- Verify `Unknown` and `Not configured` states remain visually distinct from `Matched`.
- Verify added assurance columns do not make `ReleaseDetail` or `PipelineDetail` unusable on the current desktop layout.

## Notes

- Apply `docs/pitfalls/blazor-maui.md` guidance when adding new assurance panels: guard parameter-driven loads before awaits, and use `InvokeAsync(StateHasChanged)` after asynchronous work.
- Avoid turning the Pipelines tab into a dense dashboard of every metric at once. Assurance should sharpen the current operator workflow, not replace it.
- The current tab model already divides concerns well; prefer enriching existing tabs before adding another top-level tab.
