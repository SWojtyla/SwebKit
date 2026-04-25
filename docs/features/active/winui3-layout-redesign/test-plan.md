# Test Plan - winui3-layout-redesign

---

title: "Test Plan - winui3-layout-redesign"
owner: ""
status: "Review"
created: "2026-04-25"
updated: "2026-04-25"

---

## Goal

Validate that the redesigned WinUI shell and shared page primitives improve consistency, materially increase usable main-content space, and unblock parity work without destabilizing navigation, theming, or first-load behavior.

## Scope

- In scope: shared page-state primitives, dashboard/layout adoption, settings-frame adoption, shell spacing/alignment updates, compact-header behavior, and content-area proportion checks
- Out of scope: domain-specific feature completion and Monaco/editor hosting

## Main scenarios (priority)

1. Scenario: the redesigned header and shell chrome stop crowding the main workspace. Expected result: Dashboard and Settings expose materially more visible task content without losing title, primary actions, or critical state.
2. Scenario: shared primitives replace the current repeated page-local state and card structures. Expected result: Dashboard and Settings render through shared surfaces instead of bespoke layouts.
3. Scenario: the redesigned shell still supports routed navigation and deferred initial loads. Expected result: `build-winui` stays green and routed pages keep their current lifecycle behavior.
4. Scenario: the layout redesign improves downstream reuse. Expected result: later feature slices can point to concrete compact-header and content-area primitives instead of inventing new page-level composition.

## Automated coverage

- Build validation: `build-winui` must stay green after every primitive or XAML adoption change. This is complete for the shared-primitives slice, Dashboard adoption, Settings adoption, and shell-spacing pass.
- Unit tests: keep `tests/SwebKit.WinUI.Tests/` green while layout primitives move. The current focused suite passed after the redesign changes.
- Regression target: keep the existing WinUI readiness tests green while shared layout code moves.

## Test data and setup

- Demo mode is sufficient for the first validation pass.
- Use the current Dashboard and Settings routes as the reference adoption surfaces.

## Manual checks

- Check: content-first proportion. Steps: open Dashboard and Settings at a normal desktop window size and confirm the top header stack no longer pushes the primary work surface below the fold.
- Check: shell layout coherence. Steps: open Dashboard and Settings, verify headers, actions, cards, and state regions follow the same layout language.
- Check: navigation stability. Steps: move across Dashboard, Settings, and one downstream route after the redesign lands; confirm no page breaks or unexpected spacing regressions.

## Regression risks & mitigations

- Risk: layout changes accidentally couple to page activation logic. Mitigation: keep lifecycle scheduling in `DeferredPageLoadScheduler` and validate route activation after each adoption.
- Risk: shared controls lock in the wrong abstraction. Mitigation: start with reference pages and widen only after they prove reusable.
- Risk: compact-header work simply moves noise around without increasing real work area. Mitigation: validate the visible content region explicitly in the reference pages.

## Acceptance criteria

- Dashboard and the Settings frame use the new shared layout primitives.
- The agreed compact-header rule is visible in the reference pages and gives more space to the primary work surface.
- The new primitives remove concrete duplication that the downstream parity features would otherwise repeat.
- `build-winui` remains green and the existing WinUI tests stay green.

## Validation status

- Automated: Complete — `build-winui` and `tests/SwebKit.WinUI.Tests` are green after the layout redesign implementation.
- Manual: Pending Dashboard and Settings content-density walkthrough.

## Sign-off

- **Approved by:**
- **Date:**
- **Conditions (if any):**
