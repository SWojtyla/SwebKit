# Status - AKS Enhancements v2

---

title: "Status - AKS Enhancements v2"
owner: ""
state: "In Progress"
branch: "sw/main/aks"
started: "2026-03-11"
last_updated: "2026-03-11"

---

## Quick summary

Second enhancement phase for AKS: UX improvements (resizable panels, context menus), mutative operations (restart, kill, rollback), and Helm release inspection.

**Current focus:** Phase 1 — context menus, confirm bar, and initial mutative operations.

## Progress checklist

- [x] Planning complete
- [x] Design reviewed
- [ ] Backend implementation (in progress)
- [ ] Frontend implementation (in progress)
- [ ] Tests
- [ ] Docs aligned
- [ ] Ready for review

## Completed

- Feature scope defined and module docs created.
- **Phase 1: Context menus and confirm bar**
  - Created `ContextMenu.razor` — generic right-click context menu component with backdrop, keyboard dismiss, cursor-positioned menu.
  - Created `AksConfirmBar.razor` — inline confirmation bar with optional typed-name guard for production environments.
  - Replaced all inline Logs/YAML buttons on Deployments, Pods, Ingresses, and Helm tabs with right-click context menus.
  - Added `RestartDeploymentAsync` to `IAksClient` — patches pod template annotation (same as `kubectl rollout restart`). Implemented in both `KubernetesAksClient` and `DemoAksClient`.
  - Added `DeletePodAsync` to `IAksClient` — graceful pod deletion. Implemented in both clients.
  - Wired Restart Deployment and Kill Pod actions through context menus with confirmation (production guard requires typing resource name).
  - Added Copy Host URL action for Ingresses.

## Remaining

- Resizable panels (log/YAML side panels).
- Scale deployment action.
- Helm release history, values, and rollback.
- Resource search/filter bar.
- Auto-refresh.
- Pod metrics (CPU/memory).
- Multi-namespace view.
- Tests for new components and operations.

## Blockers

- None.

## Validation

- Test Plan: `test-plan.md`
- Validation status: Not started

## Notes

- Mutative operations require production guard (confirmation dialogs).
- Context menus must be custom HTML/CSS (not browser native) for MAUI BlazorWebView compatibility.
