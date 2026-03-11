# Frontend Plan - AKS Enhancements v2

---

title: "Frontend Plan - AKS Enhancements v2"
owner: ""
status: "Planned"

---

## Goal

Replace inline action buttons with right-click context menus, add resizable panels, wire mutative operations with proper confirmation dialogs, and add resource filtering, auto-refresh, pod metrics, and multi-namespace browsing.

## Impacted areas

- `src/SwebKit.App/Components/Pages/AksPage.razor` + `.razor.css`
- `src/SwebKit.App/Components/Aks/PodLogView.razor` + `.razor.css`
- `src/SwebKit.App/Components/Aks/ContextMenu.razor` (new)
- `src/SwebKit.App/Components/Aks/ConfirmDialog.razor` (new)
- `src/SwebKit.App/Components/Aks/ResizablePanel.razor` (new)
- `src/SwebKit.App/Components/Aks/ResourceFilter.razor` (new)
- `src/SwebKit.App/Components/Aks/AutoRefreshToggle.razor` (new)

## UX design

### Context menus (replacing inline buttons)

All resource rows should respond to right-click with a contextual action menu. No pop-up dialogs — the menu appears inline at the cursor position and dismisses on click-outside or Escape.

**Deployments context menu:**
- View YAML
- View Logs (opens logs for first matching pod)
- Restart Deployment (rollout restart)
- Scale... (opens inline input for replica count)

**Pods context menu:**
- View YAML
- View Logs
- Kill Pod (delete with confirmation)
- Open Shell (existing functionality)

**Ingresses context menu:**
- View YAML
- Copy Host URL

**Helm releases context menu:**
- View Release YAML
- View Values
- View History
- Rollback... (opens revision picker with confirmation)

### Resizable panels

The log viewer and YAML viewer panels are currently fixed-width. Replace with a resizable split layout:
- Drag handle between main resource panel and side panel (logs/YAML)
- Persist last width in UI state
- Minimum width constraint to prevent collapsing
- Double-click drag handle to reset to default width

### Confirmation dialogs

All destructive actions show an inline confirmation bar (not a modal popup):
- Red-tinted background for destructive actions
- Shows resource name and action description
- "Confirm" + "Cancel" buttons
- Production environments show additional warning text and require typing the resource name

### Helm release inspection

- **History view**: Table showing all revisions with revision number, status, chart version, and timestamp
- **Values view**: Read-only YAML viewer showing computed values for the selected release
- **Rollback**: Revision picker dropdown with confirmation dialog

### Resource search and filtering

- Search/filter bar above each resource grid, filters rows in real-time as you type.
- Supports filtering by name (substring match), status, and free text across visible columns.
- Filter state is independent per tab (Deployments filter doesn't affect Pods filter).
- Clear button to reset filter. Matched text highlighted in results.
- Label selector filtering (e.g. `app=order-api`) as an advanced option.

### Auto-refresh

- Toggle button in the toolbar with interval dropdown (10s, 30s, 60s, off).
- When active, refreshes the current tab's data on the timer without full page reload.
- Pauses auto-refresh while a context menu, confirmation dialog, or YAML panel is open.
- Visual indicator (pulsing dot or timer countdown) showing auto-refresh is active.

### Pod resource usage (CPU/memory)

- Additional columns on the Pods tab: CPU (millicores) and Memory (Mi/Gi).
- Data sourced from `GetPodMetricsAsync`. Columns hidden if Metrics API is unavailable (no error, just absent).
- Color-coded usage bars or values: green (low), orange (moderate), red (high relative to limits).
- Tooltip showing requests vs limits vs actual usage.

### Multi-namespace view

- "All namespaces" option in the namespace dropdown (or a multi-select checkbox list).
- When active, adds a Namespace column to all resource grids.
- Resources loaded in parallel per selected namespace, merged into a single list.
- Loading indicator per namespace (some may return faster than others).
- Namespace column is clickable to filter to that single namespace.

## Tasks

- [ ] Create `ContextMenu.razor` — generic right-click context menu component
- [ ] Create `ConfirmDialog.razor` — inline confirmation bar with production guard
- [ ] Create `ResizablePanel.razor` — draggable split panel with persistence
- [ ] Replace inline Logs/YAML buttons with context menu on Deployments rows
- [ ] Replace inline Logs/YAML buttons with context menu on Pods rows
- [ ] Replace inline YAML button with context menu on Ingresses rows
- [ ] Add context menu on Helm releases rows
- [ ] Wire `RestartDeploymentAsync` through context menu with confirmation
- [ ] Wire `DeletePodAsync` (kill) through context menu with confirmation
- [ ] Wire `ScaleDeploymentAsync` through context menu with inline input
- [ ] Add Helm release history view
- [ ] Add Helm release values view
- [ ] Wire `RollbackHelmReleaseAsync` through context menu with revision picker and confirmation
- [ ] Add Copy Host URL action for Ingresses
- [ ] Make log panel resizable with drag handle
- [ ] Make YAML panel resizable with drag handle
- [ ] Persist panel widths in UI state
- [ ] Create `ResourceFilter.razor` — search/filter bar with per-tab state and clear button
- [ ] Add filter bar to Deployments, Pods, Ingresses, and Helm tabs
- [ ] Add label selector advanced filtering
- [ ] Create `AutoRefreshToggle.razor` — toolbar toggle with interval dropdown
- [ ] Wire auto-refresh timer to current tab reload
- [ ] Pause auto-refresh when dialogs/panels are open
- [ ] Add CPU/Memory columns to Pods tab from `GetPodMetricsAsync`
- [ ] Gracefully hide metrics columns when Metrics API unavailable
- [ ] Add "All namespaces" option to namespace dropdown
- [ ] Add Namespace column to grids in multi-namespace mode
- [ ] Load resources in parallel per namespace with per-namespace loading state

## Validation

- Component tests: Planned
- Manual checks: Planned (see `test-plan.md`)
