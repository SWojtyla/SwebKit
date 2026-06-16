# Test Plan - style-system-harmonization

---

title: "Test Plan - style-system-harmonization"
owner: ""
status: "Not started"
created: "2026-06-13"
updated: "2026-06-13"

---

## Goal

Validate that styling harmonization improves consistency without breaking app startup, themes, shared controls, feature workflows, accessibility, or route-level layout.

## Scope

### In scope

- Global stylesheet load order and theme token behavior.
- Shared control primitives and/or canonical global classes.
- Button, icon button, select/dropdown, field, toolbar, chip/badge, dialog action, and panel states.
- Main routed pages: Dashboard, Service Bus, AKS, Redis, Storage, Pipelines, Releases, Observability, Incident Timeline, Monitoring, Settings, API Client.
- Dark, light, and alternate theme smoke checks.
- Component tests for new shared primitives.

### Out of scope

- Backend integration behavior that does not depend on UI state.
- Visual pixel perfection across every minor component in the first implementation wave.
- Full accessibility audit outside changed controls.

## Main Scenarios

1. Scenario: App starts after stylesheet split or reorganization - Expected result: shell renders, current theme applies, no `Loading...` hang, no missing CSS errors.
2. Scenario: Dark and light themes render shared controls - Expected result: button/select/dropdown/input/chip states have readable text, visible borders, and clear focus states.
3. Scenario: API Client toolbar migration - Expected result: new collection, new request, save, import, and linked-root actions remain usable and visually consistent.
4. Scenario: AKS toolbar/action migration - Expected result: resource actions, refresh controls, and side panels keep layout and interaction behavior.
5. Scenario: Dropdown/select behavior - Expected result: native select popups remain readable, dropdown menus align correctly, close on outside click/Escape where supported, and do not hide behind panels.
6. Scenario: Dialog and destructive action buttons - Expected result: primary/secondary/danger states are consistent and production confirmations remain prominent.
7. Scenario: Empty/error/loading states - Expected result: shared components render consistently and do not regress feature-specific guidance.
8. Scenario: Keyboard interaction - Expected result: focus order is sensible, icon-only controls expose accessible labels, and focus rings are visible.

## Automated Coverage

- Unit/component tests: `tests/SwebKit.App.Tests` for new shared components and migrated high-risk controls.
- Existing focused component tests: rerun affected API Client, AKS, Monitoring, Incident Timeline, and Service Bus tests where components are migrated.
- E2E smoke: `tests/SwebKit.E2E.Tests` route startup/navigation checks after global CSS entry point changes.
- Static inventory: add a script or test helper that reports old token usage and raw feature-local button/select class families after each wave.

## Test Data and Setup

- Use demo mode or existing app test fixtures where integration clients are not required.
- Use representative themes: default dark, default light, and at least one alternate dark theme.
- Use focused test slices for migrated features before broader app validation.

## Manual Checks

- Check: app shell and top/left/status bars - Steps: launch app, switch themes, collapse/expand nav, verify no text overlap.
- Check: API Client toolbar and request builder - Steps: open `/api-client`, inspect toolbar buttons, tabs, dropdown/select fields, response panel, and dialog actions.
- Check: AKS diagnostics page - Steps: open AKS route in demo or configured mode, inspect toolbar, grids, context menus, panels, and loading/error states.
- Check: Service Bus page - Steps: inspect namespace panel, message list toolbar, modals, confirmation dialogs, and context menu actions.
- Check: Observability and Incident Timeline - Steps: inspect `RoutePageHeader`, `PageToolbar`, selects, toggles, pills, and refresh actions.
- Check: Monitoring drawer - Steps: create/edit rule flow, verify fields, source selects, radio/switch controls, and footer buttons.
- Check: light theme native selects - Steps: open each migrated select and verify the OS popup remains readable.

## Regression Risks & Mitigations

- Risk: Blazor CSS isolation prevents shared styles from applying inside child components. Mitigation: use global primitive classes only on rendered elements or place local styles beside child components.
- Risk: changing `app.css` load order breaks theme tokens. Mitigation: preserve current token-before-component ordering and validate all configured themes.
- Risk: alias removal breaks older feature CSS. Mitigation: add deprecation aliases first and remove only after search confirms no usage.
- Risk: global button reset changes feature-specific interactions. Mitigation: constrain global resets to primitive classes where possible.
- Risk: E2E route startup failures hide CSS regressions. Mitigation: first confirm Blazor mounted, per `docs/pitfalls/blazor-maui.md` BL-13.

## Acceptance Criteria

- `app.css` no longer acts as a feature-style bucket; it is either split into named style layers or reorganized with clear ownership sections.
- Canonical control variants exist for buttons, icon buttons, selects/dropdowns, fields, toolbars, chips/badges, and dialog actions.
- API Client and at least one AKS slice are migrated to prove the pattern works for complex features.
- Old duplicate helper classes and undefined token references are either removed or mapped through documented aliases.
- Focus, disabled, hover, active, loading, and destructive states are visually consistent across migrated surfaces.
- Focused component tests pass for new shared primitives and migrated controls.
- Manual route/theme smoke checks pass for affected pages.

## Validation Status

- Automated: Not started
- Manual: Not started

## Sign-Off

- **Approved by:**
- **Date:**
- **Conditions (if any):**