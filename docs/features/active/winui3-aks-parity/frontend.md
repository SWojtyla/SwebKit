# Frontend Module - winui3-aks-parity

---

title: "Frontend Module - winui3-aks-parity"
owner: ""
status: "In Progress"
created: "2026-04-27"
updated: "2026-04-27"

---

## Purpose

Describe the remaining WinUI AKS parity work at the page and view-model layer so implementation can proceed in narrow, reviewable slices instead of another oversized AKS rewrite.

## Current baseline

- The native AKS page already covers broad resource browse, selected-resource facts, YAML, diagnostics, monitoring, events, workload logs, pod logs, shell, and port-forward flows.
- The current page is materially closer to MAUI than the original WinUI route, but known operator regressions remain in startup scope restore, namespace search, warning behavior, log ergonomics, and row-level action discoverability.
- The current AKS page has already started addressing these gaps, so the next work should finish and validate them rather than reopen the overall layout again.

## Affected files

- `src/SwebKit.WinUI/Views/Aks/AksPage.xaml`
- `src/SwebKit.WinUI/Views/Aks/AksPage.xaml.cs`
- `src/SwebKit.WinUI/ViewModels/Aks/AksPageViewModel.cs`
- `src/SwebKit.WinUI/ViewModels/Aks/AksPageViewModel.Resources.cs`
- `src/SwebKit.WinUI/ViewModels/Aks/AksPageViewModel.PodLogs.cs`
- `src/SwebKit.WinUI/ViewModels/Aks/AksPageViewModel.WorkloadLogs.cs`
- `src/SwebKit.WinUI/ViewModels/Aks/AksPageViewModel.PortForwards.cs`
- `src/SwebKit.WinUI/ViewModels/Aks/AksPageViewModel.ResourceActions.cs`
- `src/SwebKit.WinUI/Controls/Shared/PageScaffold.xaml`
- `src/SwebKit.WinUI/Controls/Shared/PageScaffold.xaml.cs`

## Workstreams

### 1. Startup and selector parity

Goal: the AKS page should reopen on the same operator scope and keep namespace selection efficient on large clusters.

Planned work:

- Ensure first-load context and namespace selection always seed from persisted AKS settings before bootstrap side effects fire.
- Verify that context and namespace changes continue to persist back through the existing app-state path.
- Keep namespace search restored in the top toolbar and verify that it behaves well with large namespace lists.
- Confirm that all-namespaces and row-namespace flows still behave correctly after startup seeding.

Validation:

- Focused `AksPageViewModelTests` for first-load scope restore and namespace changes.
- Manual check against a profile with a saved non-default namespace.

### 2. Compact layout and warning behavior

Goal: the page should feel like MAUI's content-first workspace instead of a stacked settings screen.

Planned work:

- Keep the header collapsed when the page does not need title or subtitle chrome.
- Preserve the compact toolbar-first layout and avoid reintroducing top-of-page cards that push the explorer below the fold.
- Keep inactive port-forward UI collapsed so the diagnostics surface only expands when the operator actually opens it.
- Suppress partial-load warnings when the explorer already has usable data, while still surfacing real fully-blocking load failures.

Validation:

- `build-winui` plus focused view-model coverage for warning visibility.
- Manual review at normal desktop height to confirm no unwanted vertical-body scrollbar and no oversized inactive panels.

### 3. Log workspace parity

Goal: workload and pod logs must support real investigation without feeling squeezed into a leftover panel.

Planned work:

- Keep the log viewers vertically structured instead of compressing multiple controls into one horizontal strip.
- Make log text regions tall enough to behave like a primary investigation surface.
- Expose an explicit close action that cleanly returns the page to explorer mode.
- Verify that log controls do not fight keyboard focus, pod selection, or port-forward visibility.

Validation:

- Focused test coverage for open, reload, clear/close, and selection-driven visibility changes.
- Manual review on long pod and workload logs.

### 4. Row actions and secondary-action parity

Goal: operators should be able to act from the list itself, not only from the detail pane.

Planned work:

- Keep the current row context-flyout surface for YAML, logs, shell, port-forward, restart, and delete.
- Audit the MAUI row and detail actions that are still missing from WinUI discoverability and decide which belong in row context versus the selected-resource rail.
- Add item-scoped command entry points only where the underlying WinUI command already exists and the action is safe for row-first invocation.
- Preserve confirmation and disposal/cancellation behavior for destructive or long-running actions.

Validation:

- Focused command-routing tests for item-scoped actions.
- Manual right-click validation across Pods, workloads, and non-pod resources.

## Explicit non-goals

- Rewriting AKS resource loading or bootstrap architecture.
- Creating new cluster-management capabilities that do not already exist in MAUI.
- Reopening unrelated shell or shared-control redesign work.

## Exit criteria

- Saved context and namespace restore correctly on first page load.
- Namespace search is available and usable.
- Partial-load warnings no longer dominate the page when the explorer has usable data.
- Log surfaces feel fullscreen-leaning and close cleanly.
- Row-level actions cover the agreed MAUI action parity set.
- `build-winui` and focused AKS tests pass, followed by live-cluster manual validation.