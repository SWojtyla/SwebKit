# Frontend Plan - AKS Enhancements

---

title: "Frontend Plan - AKS Enhancements"
owner: ""
status: "In Progress"

---

## Goal

Deliver a usable AKS browser UX with context/namespace selectors, resource tabs, and read-only YAML inspection.

## Impacted areas

- `src/SwebKit.App/Components/Pages/AksPage.razor` + `.razor.css`
- `src/SwebKit.App/Components/Aks/PodLogView.razor` + `.razor.css`
- `src/SwebKit.App/Components/Pages/AksConfigForm.razor` (Settings only)
- `src/SwebKit.App/Components/Layout/LeftNav.razor`
- `src/SwebKit.App/wwwroot/app.css`

## UX notes

- Replace free-text context entry with discovered context selector.
- Keep namespace selector explicit and sticky.
- Use per-tab loading/empty/error states.
- Keep YAML viewer read-only with strong readability and responsive layout.

## Delivered

### AksPage redesign
- **Toolbar layout**: Connection status dot, context label, namespace dropdown, resource type tabs (Deployments / Pods / Ingresses), refresh button — all in a compact horizontal toolbar.
- **Namespace dropdown**: `<select>` populated from `GetNamespacesAsync()`. Changing namespace triggers a full parallel reload of all resource types and events.
- **Resource type tabs**: Button-style tab switcher in toolbar. Each tab renders a dedicated `FluentDataGrid` with type-appropriate columns:
  - Deployments: Name, Ready (pill badge), Status, Logs action
  - Pods: Name, Phase, Ready, Node, Age, Logs action
  - Ingresses: Name, Class, Hosts, Address, Path count
- **Collapsible events panel**: Close button collapses to a thin vertical strip showing "Events (N warnings)". Click to re-expand. CSS grid adapts via `.events-collapsed` class.
- **Config removed from AksPage**: Connection config lives exclusively on the Settings page. AksPage shows "Go to Settings" link when unconfigured.

### PodLogView redesign
- Live-tailing pulse indicator (green dot with animation)
- Line count display in toolbar
- Empty state when no logs
- Hover highlights on log lines
- Error lines get subtle red background tint
- Better monospace font stack (Cascadia Code > Consolas > Fira Code)

### Scoped CSS
- All inline styles eliminated from AksPage and PodLogView
- New `AksPage.razor.css` and `PodLogView.razor.css` scoped stylesheets
- Ready badges with green/orange tinted backgrounds
- Status coloring (running=green, error=red, other=muted)
- Ingress hosts highlighted in accent color

### Navigation fixes
- AKS nav icon changed from gear to Kubernetes wheel (☸)
- Settings nav item no longer uses `position: absolute` — left-nav is now flex column

## Tasks

- [x] Add namespace list selector sourced from cluster
- [x] Implement tab views for pods/deployments/ingresses
- [x] Implement collapsible events panel
- [x] Remove inline AKS config (config only in Settings)
- [x] Complete UI/UX overhaul with scoped CSS
- [x] Fix nav icon and Settings layout
- [ ] Add discovered context selector on AKS page
- [ ] Add row action to open YAML viewer per resource
- [ ] Add helm releases tab
- [ ] Add component tests for selector and tab behavior

## Validation

- Component tests: Planned
- Manual checks: Planned (see `test-plan.md`)
