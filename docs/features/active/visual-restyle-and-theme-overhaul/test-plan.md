# Test Plan - visual-restyle-and-theme-overhaul

---

title: "Test Plan - visual-restyle-and-theme-overhaul"
owner: "GitHub Copilot"
status: "Review"
created: "2026-04-15"
updated: "2026-04-16"

---

## Goal

Validate that the visual overhaul improves polish and consistency without regressing usability, theme persistence, operator safety cues, or core page workflows.

## Scope

- In scope: theme switching and persistence, shell surface consistency, shared table behavior, key page adoption, keyboard focus visibility, and desktop window resizing.
- Out of scope: backend integration correctness beyond smoke-level UI verification, exploratory redesign outside the approved plan, and unrelated feature behavior changes.

## Main scenarios (priority)

1. Scenario: Load the app with no saved theme or with a legacy dark theme value. - Expected result: `Studio Ledger` loads as the default dark direction and legacy dark values normalize cleanly to that design.
2. Scenario: Switch between supported themes in Settings and restart the app. - Expected result: selected theme persists and the shell, dialogs, forms, and page surfaces update consistently.
3. Scenario: Open Dashboard, AKS, Storage, Service Bus, Pipelines, Observability, Redis, and Settings. - Expected result: the empty route-page header shell is gone and the top bar provides the shared page title/context without leaving a blank surface at the start of the page.
4. Scenario: Use a representative table-heavy workflow in AKS, Service Bus, Storage, Pipelines/Releases, Redis, and Observability. - Expected result: headers are clearer, sorting/filter states are readable, row focus/selection states are consistent, and tables remain usable with long values.
5. Scenario: Resize the desktop window from narrow to wide operator layouts. - Expected result: shell structure remains stable, tables scroll predictably, and important controls do not overlap or disappear.
6. Scenario: Operate in production-marked configurations and destructive workflows. - Expected result: production cues remain visible and are not visually diluted by the restyle.
7. Scenario: Navigate with keyboard through top-level shell actions, filters, and shared tables. - Expected result: focus indication is visible and interaction order remains understandable.

## Automated coverage

- Unit tests: `tests/SwebKit.App.Tests` - focused restyle coverage now exercises AKS toolbar/select seams, Service Bus namespace and message-list flows, Storage download/detail behavior, Template Picker behavior, and Observability guided logs behavior.
- Integration tests: n/a for planning; use focused component-level validation where UI state or theme persistence crosses component boundaries.
- End-to-end tests: `tests/SwebKit.E2E.Tests` builds cleanly after the theme-normalization helper updates. The attached-session Playwright smoke run is still environment-sensitive because the fixture reuses an already-running app session and can inherit stale in-memory theme state.

## Test data and setup

- Use existing demo-mode and mocked data paths wherever possible for visual regression and interaction checks.
- Maintain at least one representative dataset with long names, wrapped content, and empty states so table behavior is exercised realistically.
- Test both dark theme and at least one light theme during manual validation.

## Manual checks

- Check: Theme switch and persistence - Change theme in Settings, navigate across several pages, restart the app, and confirm the same theme remains active.
- Check: Route-header removal - Open the main routed pages and confirm the old blank header strip is gone while the shell top bar still communicates page context.
- Check: Support-strip availability - Verify pages that previously exposed scope pills or `Settings` actions still surface those controls through the compact support strip after the route-header cleanup.
- Check: Dark default - Launch the app with no saved theme or a legacy dark theme and confirm `Studio Ledger` is selected automatically.
- Check: Table header clarity - Inspect column headers, sorting cues, sticky behavior, and row hover/focus states on AKS, Storage, and Service Bus surfaces.
- Check: Long-content handling - Verify truncation, wrapping, and tooltip behavior for long resource names, blob paths, message subjects, and release identifiers.
- Check: Production cues - Enable production mode and confirm warning/danger styling remains stronger than normal accent styling.
- Check: Window resize - Validate the shell and large tables at common desktop widths such as 1280px, 1440px, and maximized layouts.
- Check: Keyboard focus - Tab through shell controls, filters, and interactive table elements and confirm focus is always visible.

## Regression risks & mitigations

- Risk: CSS isolation or child-component boundaries prevent styles from applying consistently. - Mitigation: keep shared rules in `wwwroot/app.css` and place component-specific rules in each component's own `.razor.css` file.
- Risk: inline styles continue to override shared tokens after migration. - Mitigation: explicitly inventory and remove high-impact inline color, spacing, and header styles during adoption.
- Risk: theme changes look good on one page but break contrast on another. - Mitigation: validate dark and light themes across all priority surfaces before closing the feature.
- Risk: table restyle reduces density or scan speed for operators. - Mitigation: test with real-world long-value datasets and keep readability and scanning speed as acceptance criteria.

## Acceptance criteria

- Shared theme tokens and table primitives are adopted across the planned priority surfaces.
- Theme selection remains persistent and visually coherent across shell and page surfaces.
- Table headers, row states, and sort/filter cues are clearly improved on the main table-heavy pages.
- No critical contrast, focus, or resize regressions remain open.
- Planning docs, implementation docs, and any affected architecture/functionality docs stay aligned.

## Validation status

- Automated: `get_errors` reported no diagnostics, the focused App.Tests restyle suite passed (`46/46`), and `tests/SwebKit.E2E.Tests` builds cleanly. The attached-session Playwright smoke run is not treated as authoritative sign-off because it did not relaunch the app into a fresh session.
- Manual: Not run in this session. A human visual pass in `Studio Ledger` plus one light theme is still recommended before archive/close-out.

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
