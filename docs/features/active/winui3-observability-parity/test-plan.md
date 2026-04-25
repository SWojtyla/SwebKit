# Test Plan - winui3-observability-parity

---

title: "Test Plan - winui3-observability-parity"
owner: ""
status: "Not started"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Validate that the Observability workspace reaches the agreed native parity for discovery, analysis, and query editing while staying clear under both demo-mode and live Azure credential conditions.

## Scope

- In scope: page seam reduction, query-editor path, chart and drill-through parity, readiness-to-settings repair loop, focused tab-state validation
- Out of scope: new analytics capabilities beyond MAUI parity

## Main scenarios (priority)

1. Scenario: missing Azure credentials are survivable. Expected result: Observability shows readiness guidance and opens the correct native Settings repair surface.
2. Scenario: operators can complete the planned analysis workflow natively. Expected result: discovery, tabs, charts, and the query-editor path cover the agreed MAUI parity surface.
3. Scenario: the refactored page seam is testable and stable. Expected result: discovery, tab activation, and editor state no longer compete inside one fragile state object.

## Automated coverage

- Build validation: `build-winui` must stay green.
- Existing tests: keep `tests/SwebKit.WinUI.Tests/` green.
- New tests: add focused WinUI coverage for discovery, readiness transitions, tab state, and any query-editor state introduced by the feature.

## Test data and setup

- Demo mode covers initial route and tab-state validation.
- Live validation needs Azure sign-in or equivalent credentials plus representative observability resources.

## Manual checks

- Check: readiness repair loop. Steps: trigger missing-credential behavior, open Settings from the readiness action, repair the environment, and reload Observability.
- Check: analysis parity. Steps: exercise the agreed discovery, tab, chart, and query-editor workflow and confirm it matches the planned native surface.

## Regression risks & mitigations

- Risk: editor hosting destabilizes page activation or tab changes. Mitigation: validate discovery, navigation, and editor state separately.
- Risk: chart parity work hides readiness regressions. Mitigation: keep credential-readiness scenarios as first-class acceptance criteria.

## Acceptance criteria

- The agreed Observability workflow, including the query-editor path, is available natively.
- Readiness repair loops through native Settings successfully.
- `build-winui` and focused WinUI tests stay green.

## Validation status

- Automated: Not started
- Manual: Not started

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
