# Test Plan - AKS Enhancements v2

---

title: "Test Plan - AKS Enhancements v2"
owner: ""
status: "Planned"
created: "2026-03-11"
updated: "2026-03-11"

---

## Goal

Validate mutative AKS operations, context menu UX, resizable panels, and production safety guards.

## Scope

- In scope: context menus, confirmation dialogs, pod delete, deployment restart/scale, Helm rollback/history/values, resizable panels, resource filtering, auto-refresh, pod metrics, multi-namespace view
- Out of scope: full YAML editing, management-plane operations

## Main scenarios (priority)

1. Scenario: Right-click deployment row — Expected result: context menu appears with View YAML, View Logs, Restart, Scale options.
2. Scenario: Restart deployment — Expected result: confirmation shown, after confirm deployment pods restart, status updates.
3. Scenario: Kill pod — Expected result: confirmation shown, pod deleted, pod list refreshes.
4. Scenario: Helm rollback — Expected result: revision picker shown, confirmation with revision details, rollback executes, release list refreshes.
5. Scenario: Production guard — Expected result: destructive actions on production environments require typing resource name to confirm.
6. Scenario: Resize log panel — Expected result: drag handle moves panel boundary, width persists across page navigations.
7. Scenario: Helm release values/history — Expected result: values YAML and revision history displayed correctly.
8. Scenario: Filter resources by name — Expected result: grid rows filter in real-time, filter state persists per tab.
9. Scenario: Auto-refresh toggle — Expected result: data refreshes on interval, pauses when dialog open, visual indicator visible.
10. Scenario: Pod metrics display — Expected result: CPU/memory columns shown when Metrics API available, hidden gracefully when not.
11. Scenario: Multi-namespace view — Expected result: "All namespaces" shows merged resources with Namespace column, parallel loading per namespace.

## Automated coverage

- Unit tests: deployment restart patch, pod delete, Helm history/values parsing, pod metrics parsing, multi-namespace merge logic
- Integration tests: Deferred (requires cluster or mocked API)
- Component tests: context menu positioning/dismissal, confirmation dialog states, resizable panel drag, filter bar state, auto-refresh timer, metrics column visibility

## Test data and setup

- DemoAksClient provides realistic responses for all new operations
- Fixtures: Helm release secret data for history/values decoding

## Manual checks

- Check: context menu positioning near viewport edges — steps: right-click rows near bottom/right edges.
- Check: production guard flow — steps: switch to production environment and attempt destructive action.
- Check: resizable panel persistence — steps: resize, navigate away, return.
- Check: filter clears on namespace/context switch — steps: set filter, switch namespace, verify filter reset.
- Check: auto-refresh pauses during confirmation — steps: enable auto-refresh, trigger kill pod, verify no refresh during confirm dialog.
- Check: multi-namespace with large cluster — steps: select "All namespaces" on cluster with 10+ namespaces, verify responsive loading.

## Regression risks & mitigations

- Risk: context menu breaks existing row selection — Mitigation: separate right-click handler from row click.
- Risk: destructive operations called without confirmation — Mitigation: confirmation is enforced in the UI layer, not optional.
- Risk: auto-refresh causes data flicker during user interaction — Mitigation: pause refresh when dialogs/panels are open.
- Risk: Metrics API unavailable causes error spam — Mitigation: single check on connect, cache availability, hide columns silently.

## Acceptance criteria

- All priority scenarios pass
- No regressions in read-only resource browsing
- Production guard prevents accidental destructive actions
- Context menus work correctly in MAUI BlazorWebView

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- Owner:
- Date:
