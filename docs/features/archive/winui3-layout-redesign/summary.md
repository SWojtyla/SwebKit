# Archive Summary - winui3-layout-redesign

---

title: "Archive Summary - winui3-layout-redesign"
owner: ""
jira: "not linked"
completed_date: "2026-04-26"
pr: "not linked"
commit: "not captured"

---

## Goal

Redesign the WinUI shell and shared page layout so the migration lands on a consistent, content-first native information architecture instead of page-local XAML patterns.

## Delivered

- Added shared WinUI primitives for `StateView`, `MetricCard`, `SectionCard`, and `DetailPaneHost`.
- Extended `PageScaffold` with compact-header and inline-context options so secondary guidance stops consuming excessive vertical space.
- Rebuilt Dashboard and the Settings frame on the shared layout language and tightened shell, banner, context-header, badge, icon-button, and workspace-hub spacing.
- Established a reusable content-first layout baseline that downstream parity slices can consume without reopening shell-wide layout decisions.

## Key decisions

- Keep the feature focused on reusable shell-wide and page-shared primitives instead of absorbing page-local composition work from the domain parity slices.
- Use Dashboard and Settings as the reference adopters before widening the layout contract across the rest of the WinUI migration.
- Accept the remaining visual walkthrough evidence as part of the final cross-feature cutover review rather than blocking archive on a feature-local manual pass.

## Validation performed

- Build validation: `build-winui` stayed green across the shared-primitives slice, Dashboard adoption, Settings adoption, and shell-spacing pass.
- Unit tests: the focused `tests/SwebKit.WinUI.Tests` suite passed after the redesign changes.
- Reuse evidence: `winui3-aks-parity` documents downstream adoption of `SectionCard`, `MetricCard`, `StateView`, and `DetailPaneHost` in the native AKS explorer and detail-pane flow.
- Manual checks: remaining Dashboard and Settings visual walkthrough evidence is intentionally deferred to the final end-to-end WinUI review on 2026-04-26.

## Lessons learned

- Shared layout primitives need to land before broad parity work, otherwise each domain page invents bespoke spacing and state composition.
- A downstream consumer like AKS provides better proof of layout reuse than keeping layout evidence trapped in the original reference pages.

## Follow-up

- Final visual walkthrough evidence for Dashboard, Settings, and wider native shell coherence — owner: `winui3-cutover-audit-hardening`

## Archive note

> This file is present because the feature had no Jira ticket. Archive location: `docs/features/archive/winui3-layout-redesign/`.