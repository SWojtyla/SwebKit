# Frontend Plan - windows-tray-aks-monitoring

---

title: "Frontend Plan - windows-tray-aks-monitoring"
owner: "GitHub Copilot"
status: "Not started"

---

## Goal

Deliver clear user-facing behavior for tray mode: users can minimize/close to tray, continue AKS monitoring in background, and restore or exit intentionally from tray controls.

## Impacted areas

- AKS monitoring page and monitor affordances:
- src/SwebKit.App/Components/Pages/AksPage.razor
- src/SwebKit.App/Components/Aks/NamespaceMonitorSelector.razor
- src/SwebKit.App/Components/Aks/AlertHistoryPanel.razor
- Shell/status area (if visible affordance is added):
- src/SwebKit.App/Components/Layout/MainLayout.razor
- src/SwebKit.App/Components/Layout/StatusBar.razor
- Windows native tray UX artifacts:
- src/SwebKit.App/Platforms/Windows/
- Optional tray icon assets in:
- src/SwebKit.App/Resources/

## UX notes

- User flows:
- Minimize and Close both send app to tray.
- Tray menu offers Restore and Exit.
- While hidden, AKS pod alerts still surface via Windows toast and tray indicator.
- On restore, user can continue normal navigation and open AKS monitor panel to inspect alert history.

- Component states:
- Hidden-to-tray state should not present error popups or blocking dialogs.
- Restored state should preserve current navigation and UI state (tabs, filters, selected area) where feasible.
- Alert indicator reset rule must be deterministic (for example, reset on restore or on monitor panel open).

- Accessibility:
- Ensure tray menu labels are clear and unambiguous (Restore, Exit).
- Keep keyboard-first workflow intact after restore (focus management in shell).

## API / contract changes

- No external API changes.
- Internal UI contract alignment may include event wiring between tray indicator state and existing AKS alert surfaces.
- Preserve existing `IPodHealthMonitorService` event semantics; UI consumes existing event stream.

## Tasks

- [ ] Define user-visible tray behavior contract (restore, exit, unread indicator reset rule).
- [ ] Add/adjust AKS page badge sync behavior with tray indicator state if needed.
- [ ] Ensure restoring from tray does not disrupt current page-level state.
- [ ] Add first-run user guidance for close-to-tray behavior (toast/info hint).
- [ ] Add/update component/service tests for indicator reset and restore flow.
- [ ] Add manual UX checks for repeated hide/restore cycles.
- [ ] Record non-obvious UX decisions in `decisions.md`.

## Validation

- Component tests: Not started
- Manual UX checks:
- Validate Minimize and Close behavior across AKS and non-AKS pages.
- Validate alert visibility while hidden and state consistency after restore.

## Notes

- Existing AKS alert history UI already consumes monitor events; tray behavior should complement this rather than duplicating alert storage.
- Do not require users to stay on `AksPage` for monitoring continuity.
