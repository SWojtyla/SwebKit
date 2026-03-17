# Test Plan — AKS Enhancements (Batch 2)

---

title: "Test Plan — AKS Enhancements Batch 2"
owner: ""
status: "Done"
created: "2026-03-17"
updated: "2026-03-17"

---

## Goal

Verify that the seven UX improvements work correctly, that existing AKS page behaviour
is not regressed, and that the build stays clean.

## Scope

**In scope:**

- Unit tests for new backend methods (`GetCronJobsAsync`)
- Build verification for all affected projects
- Manual UX validation for layout, search, URL click, and CronJobs grid

**Out of scope:**

- E2E tests against a live Kubernetes cluster
- Visual regression testing
- Accessibility audit (follow-up)

## Main scenarios (priority)

1. **Side-panel layout** — Open YAML viewer, then open HPA detail — both panels should be visible without overflowing below the grid. Expected: single `aks-panels-col` column on the right, no layout wrapping.

2. **Events collapsed by default** — On page load, events section is not shown. Thin vertical tab appears on right edge. Click tab → events expand at bottom of panel column. Expected: no horizontal space consumed until opened.

3. **Events in panel column** — Open YAML viewer, then expand events — events appear at the bottom of the same column as the YAML panel, not as a separate column. Expected: unified layout.

4. **Auto-refresh pauses with events open** — Expand events; the `AutoRefreshToggle` indicator shows paused state. Collapse events; auto-refresh resumes. Expected: `HasAnyPanel` controls pause correctly.

5. **YAML search** — Open any resource YAML. Click the search toggle button. Type a search term that appears in the YAML. Expected: matching tokens highlighted in yellow, match count shown, first match scrolled into view. Clear button resets state.

6. **Ingress URL click** — Navigate to Ingresses tab. Click a host cell. Expected: default OS browser opens with the inferred URL (`https://` for named host, `http://` for IP).

7. **Ingress context menu** — Right-click an Ingress row. Menu should show "Open URL in browser" and "Copy URL" items. Both should work.

8. **Pod CPU/Mem columns always visible** — Switch to Pods tab with metrics server unavailable (or in demo mode). Expected: CPU and Memory columns are present in the grid header; cells show "—".

9. **Pod CPU/Mem with real data** — When metrics are available, cells show formatted values (not "—").

10. **Helm history order** — Open Helm history for any release. Expected: most recent revision is the first row.

11. **CronJobs tab** — Select "CronJobs" resource tab. Expected: grid loads with Name, Schedule, Active, Last Schedule, Last Success columns. Suspended CronJob shows a "suspended" badge and muted name style.

12. **CronJob YAML** — Right-click a CronJob row, select "View YAML". Expected: YAML panel opens with the CronJob resource YAML.

13. **CronJob filter** — Type in the filter box on the CronJobs tab. Expected: rows filtered by name or schedule.

## Automated coverage

- **Unit tests (`SwebKit.Core.Tests`):** 113/113 passing. `DemoAksClientTests` exercises `GetCronJobsAsync` indirectly. A dedicated test case for `GetCronJobsAsync` return value (count, field correctness) is noted as a follow-up in `backend.md`.
- **Build:** `SwebKit.App`, `SwebKit.Core`, `SwebKit.Kubernetes` — 0 errors.
- **Component tests:** Existing `AksPage` component tests continue to pass; layout changes do not break selector-based assertions.

## Test data and setup

- **Demo mode:** All manual checks above can be performed using `DemoAksClient` (set via the app's environment selector). No live cluster required for initial validation.
- **Suspended CronJob:** Demo data includes `audit-log-archiver` with `Suspend = true`.
- **Metrics unavailable:** In demo mode, `PodMetricsList` is intentionally populated with some entries missing to exercise the "—" path.

## Manual checks

- Check: Panel column layout — open two panels in sequence, confirm no overflow — steps: Deployments → right-click → Scale; then right-click another → View YAML
- Check: Events inset — load page, confirm no events column visible; click vertical tab; confirm events expand at bottom
- Check: YAML search — open any YAML, toggle search, type "name", confirm highlights
- Check: YAML search clear — press ✕ button, confirm highlights removed
- Check: YAML search case-insensitive — type "NAME" and "name" produce same count
- Check: Ingress host click — click host in Ingresses grid, confirm browser opens
- Check: Helm history row order — first row should be highest revision number
- Check: CronJobs grid loads — select CronJobs tab, confirm 5 rows in demo mode
- Check: Suspended badge — `audit-log-archiver` row has "suspended" pill and muted style
- Check: CronJob YAML — right-click → View YAML, confirm YAML panel opens

## Regression risks & mitigations

- Risk: Other pages using `ResizablePanel` broken by CSS changes — Mitigation: CSS changes are scoped to `AksPage.razor.css`; `ResizablePanel` component CSS is unchanged
- Risk: `CloseAllMenus` misses the new `CronJobMenu` — Mitigation: verified in code; `CronJobMenu.Close()` is called
- Risk: YAML search leaves stale `<mark>` elements after panel close — Mitigation: `CloseYaml` explicitly calls `ClearYamlSearch` which invokes `clearSearch` JS

## Acceptance criteria

- All 10 manual check items pass in demo mode
- Build: 0 errors on `SwebKit.App`
- Tests: 113/113 passing
- No layout overflow when two or more panels are open simultaneously
- CronJobs tab visible and populated in demo mode

## Validation status

- Automated: Passed (113/113 unit tests, 0 build errors)
- Manual: Pending explicit sign-off

## Sign-off

- Owner:
- Date:
