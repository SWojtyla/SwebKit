# Feature Overview - AKS Enhancements v2

---

title: "Feature Overview - AKS Enhancements v2"
owner: ""
status: "Planned"
created: "2026-03-11"
updated: "2026-03-11"

---

## Goal

Elevate the AKS page from a read-only resource browser to a practical daily-driver for Kubernetes troubleshooting and operations with improved UX, context menus, resizable panels, and targeted mutative actions.

## Value

Developers can perform routine cluster operations (restart deployments, kill pods, rollback Helm releases) and inspect resources with a polished, keyboard-and-mouse-friendly UX — without leaving SwebKit or opening a terminal.

## Scope

- In scope:
  - Resizable log and YAML panels
  - Right-click context menus on all resource rows (replacing inline buttons)
  - Deployment actions: restart (rollout restart), scale
  - Pod actions: kill (delete), view logs, view YAML, open shell
  - Ingress actions: view YAML, copy host URL
  - Helm actions: view release YAML/values, rollback to previous revision
  - UX polish: better empty states, keyboard shortcuts, confirmation dialogs for destructive actions
  - Resource search and filtering across all tabs
  - Auto-refresh with configurable interval
  - Pod resource usage (CPU/memory) via Metrics API
  - Multi-namespace view
- Out of scope:
  - Full YAML editing and apply
  - AKS management-plane operations (nodepool management, cluster upgrades)
  - CI/CD integration

## Current state

Planning phase. All prerequisite work from v1 (resource browsing, YAML viewer, context/namespace selectors) is complete and archived.

## Dependencies

- Archived features: `docs/features/archive/aks/`, `docs/features/archive/aks-enhancements/`
- `KubernetesAksClient` and `IAksClient` contract extensions
- Existing app shell/state infrastructure

## Risks & mitigations

- Risk: destructive operations on production clusters — Mitigation: production guard with explicit confirmation dialogs, visual warnings on production environments
- Risk: Helm rollback complexity across chart variations — Mitigation: rollback by revision only (Helm native), no custom chart manipulation
- Risk: context menu rendering in MAUI BlazorWebView — Mitigation: custom HTML/CSS context menu (not browser native), test on Windows early
- Risk: Metrics API not available on all clusters — Mitigation: graceful fallback, hide CPU/memory columns when metrics unavailable
- Risk: multi-namespace queries slow on large clusters — Mitigation: limit to selected namespaces, parallel API calls, loading indicators per namespace

## Related documents

- Archive: `docs/features/archive/aks-enhancements/summary.md`
- Architecture: `docs/architecture/architecture.md`, `docs/architecture/design.md`
- Pitfalls: `docs/pitfalls/blazor-maui.md`

## Suggested modules

- `backend.md` — new IAksClient methods, Helm rollback, pod delete, deployment restart
- `frontend.md` — context menus, resizable panels, confirmation dialogs, UX polish
- `decisions.md` — v2 tradeoffs
- `test-plan.md` — test coverage for mutative operations
- `status.md` — implementation progress

## Quick links

- Status: `status.md`
- Backend plan: `backend.md`
- Frontend plan: `frontend.md`
- Tests: `test-plan.md`
- Decisions: `decisions.md`
