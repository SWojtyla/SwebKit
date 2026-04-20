# Frontend Plan — aks-workspace-polish

---

title: "Frontend Plan — aks-workspace-polish"
owner: ""
status: "Not started"

---

## Goal

Deliver 11 targeted UX and visual improvements to the AKS workspace: severity-coloured logs, unhealthy-row tinting, filterable events with jump-to-resource, dynamic keyboard hints, CronJob next-run countdowns, a namespace quick-chip, port-forward browser open and pinned targets, wired Helm diff, YAML pre-validation, and container resource comparison.

## Impacted areas

| Item | File(s)                                                                                                                                                       |
| ---- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| #1   | `Components/Aks/PodLogView.razor`, `PodLogView.razor.css`                                                                                                     |
| #2   | `Components/Aks/PodGrid.razor`, `PodGrid.razor.css`, `DeploymentGrid.razor`, `DeploymentGrid.razor.css`, `StatefulSetGrid.razor`, `StatefulSetGrid.razor.css` |
| #3   | `Components/Aks/AksDetailPanels.razor` — events section (no dedicated file yet; inline in detail panels)                                                      |
| #4   | `Components/Pages/AksPage.razor` — `aks-kbd-hints` bar                                                                                                        |
| #5   | `Components/Aks/CronJobGrid.razor`, `CronJobGrid.razor.css`                                                                                                   |
| #6   | `Components/Aks/AksConnectionBar.razor`, `AksConnectionBar.razor.css`                                                                                         |
| #10  | `Components/Aks/PortForwardSessionsPanel.razor`                                                                                                               |
| #11  | `Components/Aks/PortForwardStartDialog.razor` (load/save pins); reads `UserSettings` via DI                                                                   |
| #13  | `Components/Aks/AksHelmPanel.razor` — wire `HelmDiffPreviewPanel` into revision selection                                                                     |
| #14  | `Components/Aks/AksYamlViewer.razor` — add pre-validate step before Apply                                                                                     |
| #16  | `Components/Aks/ContainerDetailPanel.razor` — add resource usage comparison row                                                                               |

---

## Item detail

### #1 — Log level-aware line colouring

**Approach:** In `PodLogView.razor` the log buffer builds `LogEntry` records that already carry a `CssClass` field. Add a static helper `ClassifyLogLevel(string line)` that scans the first 120 characters for known level keywords (`ERROR`, `FATAL`, `CRITICAL`, `WARN`, `WARNING`, `DEBUG`, `TRACE`) — case-insensitive, word-boundary match — and maps to one of: `log-level-error`, `log-level-warn`, `log-level-debug`, or `log-level-default`.

Apply the class in the Virtualize render loop (already renders `@entry.CssClass`). Add the four colour variables to `PodLogView.razor.css`.

**Edge cases:** JSON-structured lines (start with `{`) bypass the regex entirely and default to `log-level-default`. Lines with no recognized level keyword → `log-level-default`.

---

### #2 — Status row tinting

**Approach:** Each grid (`PodGrid`, `DeploymentGrid`, `StatefulSetGrid`) has a per-row helper that currently derives a status label. Extend it to also return a CSS row modifier:

- `row-critical` — CrashLoopBackOff, OOMKilled, Error, ImagePullBackOff, ErrImagePull, ExitCode:\*
- `row-degraded` — Pending, ContainerCreating, Terminating, ReadyReplicas < Replicas (non-zero)
- `row-muted` — Completed, Succeeded

Apply the modifier as an additional class on the `<tr>` element. Define tint colour variables in the respective `.razor.css` files using CSS custom properties so dark/light themes override correctly.

**Note (BL-1):** No new component subdirectory — these files are already in `Components/Aks/`, `_Imports.razor` needs no update.

---

### #3 — Events panel filter + jump-to-resource

**Approach:** The events panel is currently rendered inline inside `AksDetailPanels.razor`. Extend it to:

1. Add two filter chips above the list: `Warning` | `Normal` | `All` (default = All)
2. For each event where `InvolvedObjectKind` is `Deployment`, `Pod`, `StatefulSet`, `CronJob`, or `Job`, render a small "→" icon button. Clicking it calls a new `EventCallback<KubernetesEvent> OnJumpToResource` parameter bubbled up to `AksPage`. `AksPage` handles the callback by switching `ActiveResourceType` to the matching kind and setting the filter text to the resource name.
3. If the resource name is not present in the currently loaded resource list → button is disabled with title "Resource not currently loaded".

**State:** filter type stored as a local enum field on the events panel section; no persistence needed.

---

### #4 — Dynamic keyboard hint bar

**Approach:** `AksPage.razor` already computes the hint bar from `ActiveResourceType`. Extend it to also factor in the selected item state:

- Selected pod with non-running/non-completed status → dim the `s` (shell) hint via `aks-kbd-hint--dimmed` CSS class and add a tooltip: "Shell not available — pod is not running"
- Selected pod → show hint `d` (delete) only when the pod is not `Terminating` already
- Selected CronJob that is `Suspended` → dim `r` (trigger) hint with tooltip "CronJob is suspended"

The hint bar logic is already inline in `AksPage.razor`. Refactor into a private helper method `GetKbdHintState(selectedResource)` to keep the template readable.

---

### #5 — CronJob next-run countdown

**Approach:** In `CronJobGrid.razor`, add a tooltip on the schedule column cell. Compute next-run using a lightweight `CronNextRun.TryCalculate(string schedule, DateTimeOffset from, out DateTimeOffset next)` static utility class added to `SwebKit.App/Services/` (or `Components/Aks/` as a static helper — no DI needed).

Support standard 5-field Quartz/Unix cron expressions only. For unrecognised expressions (6-field, `@reboot`, custom), display the raw schedule with a `(schedule)` suffix. For `Suspended = true`, show "Suspended" regardless.

Format: `"in 4h 12m"` for < 24 h; `"in 2d 3h"` for >= 24 h. All times in UTC.

---

### #6 — Namespace "All namespaces" chip

**Approach:** In `AksConnectionBar.razor`, add a `*` chip button immediately before the namespace dropdown. When clicked it fires `OnNamespaceChanged` with the special value `"*"` (already the multi-namespace sentinel used by `AksPage`). The chip renders in an active/highlighted state when `CurrentNamespace == "*"`. When the dropdown selects a specific namespace, the chip reverts to inactive.

---

### #10 — Port-forward "Open in browser" button

**Approach:** In `PortForwardSessionsPanel.razor`, for each session with `Status == PortForwardStatus.Active`, check if `session.LocalPort` is in a known HTTP port set (`{80, 443, 3000, 4000, 5000, 7000, 8000, 8080, 8443, 9000, 9090}`). If so, render an "Open ↗" button that calls `Microsoft.Maui.ApplicationModel.Launcher.OpenAsync(new Uri($"http://localhost:{session.LocalPort}"))` via `await`. For HTTPS ports (443, 8443) use `https://`.

Use `@inject` for `IServiceProvider` to resolve `ILauncher` if mocked in tests, or call the static API directly since this is a platform feature.

---

### #11 — Pinned port-forward targets

**Frontend work:**

`PortForwardStartDialog.razor`:

- On open, load pinned entries for the active kubeconfig context from `UserSettings.PinnedPortForwards` (injected via `AppStateService` or a parameter)
- Render a "Pinned" section at the top of the dialog listing `PodLabelSelector / RemotePort → LocalPort`; clicking a pinned entry pre-fills the form fields
- After a successful port-forward start, show a "Pin this" toggle; when confirmed, call `SavePinnedAsync(entry, context)` on the new `PinnedPortForwardService` (see `backend.md`)
- Show a delete (✕) icon on each pinned entry

**PortForwardSessionsPanel.razor:** no change needed for #11.

---

### #13 — Helm diff wiring

**Approach:** `HelmDiffPreviewPanel.razor` already exists. In `AksHelmPanel.razor`, the revision list renders each row with a "Rollback" action. Add a "Diff" button on each non-current revision row. Clicking "Diff" calls `_helmPanel.OpenDiff(revision)` which populates and shows `HelmDiffPreviewPanel`.

The panel should be visible in the side panel alongside the revision list, not replacing it. Use the existing `aks-panel-pane` container pattern already used for YAML viewer, scale, and logs.

**Graceful degradation:** `HelmDiffPreviewPanel` should catch `helm diff` subprocess errors and distinguish: (a) plugin not installed → show a setup callout with `helm plugin install https://github.com/databus23/helm-diff`, (b) general diff error → show the raw error output.

Do NOT use `@if` to unmount the panel component (BL-4). Use `style="display:none"` when the diff is not active.

---

### #14 — YAML editor structural pre-validation

**Approach:** In `AksYamlViewer.razor`, before the Apply button calls `ApplyResourceYamlAsync`, add a synchronous validation step:

1. Attempt `YamlDotNet` deserialization to a `Dictionary<object, object>` to catch YAML syntax errors
2. If successful, check for required fields: `apiVersion`, `kind`, `metadata.name` — warn if any are missing
3. Surface warnings as an inline amber banner above the Apply button: "Structural issues detected — apply anyway?" with a "Apply anyway" secondary button and "Fix first" primary button
4. Hard errors (unparseable YAML) block Apply entirely until fixed

`YamlDotNet` is already referenced in `SwebKit.Kubernetes.csproj`; confirm it is also available or add the package reference to `SwebKit.App.csproj`.

---

### #16 — Container detail: requests/limits vs usage

**Approach:** `ContainerDetailPanel.razor` already calls `GetContainerDetailsAsync` which returns `ContainerDetail` records. `GetPodMetricsAsync` returns `PodMetrics` records with per-container CPU/memory usage.

Add a `PodMetrics?` parameter (nullable) to `ContainerDetailPanel`. The parent (`AksDetailPanels`) already loads `PodMetricsList` and can pass the matching `PodMetrics` for the selected pod.

In the container detail table, add two new rows per container:

- **CPU:** `Request: 100m | Limit: 500m | Current: 87m (17% of limit)`
- **Memory:** `Request: 128Mi | Limit: 512Mi | Current: 234Mi (46% of limit)`

If `ContainerDetail.Resources` is null → show "No requests/limits configured". If `PodMetrics` is null → show "Metrics unavailable" in the Current column. Use the same bar-render style as `PodGrid` (reuse the same CSS variables for CPU/memory bar colour thresholds).

---

## UX notes

- All wave 1 items must work correctly in both light and dark themes (use CSS custom properties, not hard-coded colours)
- Items #3, #4: no new loading states needed — operate on already-loaded in-memory data
- Item #11 dialog must handle loading state (briefly) while `UserSettings` is read
- Item #14 "Fix first" / "Apply anyway" pattern follows existing `AksConfirmBar` style — use the same button CSS classes (`confirm-btn confirm-yes/no`)

## API / contract changes

- `ContainerDetailPanel.razor`: add `[Parameter] public PodMetrics? PodMetrics { get; set; }` — backward compatible (nullable)
- `AksDetailPanels.razor`: pass matching `PodMetrics` for selected pod to `ContainerDetailPanel`
- `UserSettings` model change: see `backend.md` — frontend reads via injected `AppStateService`
- `AksDetailPanels.razor`: add `EventCallback<KubernetesEvent> OnJumpToResource` — `AksPage` subscribes

## Tasks

- [ ] **#1** Add `ClassifyLogLevel` helper + CSS classes to `PodLogView.razor`
- [ ] **#2** Add row tint CSS classes + row modifier helper to `PodGrid`, `DeploymentGrid`, `StatefulSetGrid`
- [ ] **#3** Extend events section in `AksDetailPanels` with filter chips + jump callback + `OnJumpToResource` handler in `AksPage`
- [ ] **#4** Refactor kbd hint bar in `AksPage` to `GetKbdHintState()` + add dimmed class + state-aware hints
- [ ] **#5** Add `CronNextRun` static helper + tooltip to `CronJobGrid`
- [ ] **#6** Add `*` chip to `AksConnectionBar`
- [ ] **#10** Add "Open in browser" button to `PortForwardSessionsPanel`
- [ ] **#11** Add pinned entries UI to `PortForwardStartDialog` + "Pin this" post-start action
- [ ] **#13** Add "Diff" button to `AksHelmPanel` revision list + wire `HelmDiffPreviewPanel` + graceful fallback
- [ ] **#14** Add YAML pre-validation in `AksYamlViewer` with inline banner
- [ ] **#16** Add `PodMetrics` param to `ContainerDetailPanel` + resource comparison rows; update `AksDetailPanels` to pass metrics
- [ ] Add `@using` for any new component subdirectory to `_Imports.razor` (BL-1)
- [ ] All `StateHasChanged()` calls inside async methods go via `InvokeAsync` (BL-2)

## Validation

- Component tests: Not started
- Manual UX checks: see `test-plan.md` for per-item acceptance steps
