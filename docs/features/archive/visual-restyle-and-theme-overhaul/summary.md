# Archive Summary - visual-restyle-and-theme-overhaul

---

title: "Archive Summary - visual-restyle-and-theme-overhaul"
owner: "GitHub Copilot"
jira: "not linked"
completed_date: "2026-04-17"
pr: ""
commit: ""

---

## Goal

Restyle SwebKit to feel more polished, pleasant, and trustworthy while keeping the global layout and route structure intact. Deliver a stronger semantic token system, a clear app-wide design language chosen through a live in-app pilot, and a shared table/surface primitive set applied across all high-visibility feature areas.

## Delivered

- **In-app art direction pilot** — Two live dark design directions (`Command Deck` vs `Studio Ledger`) exposed through Settings and visible on shell chrome, Dashboard, Storage, AKS, and Settings appearance surfaces. Direction chosen from real usage, not a static mockup.
- **Studio Ledger chosen as global dark direction** — Premium slate-metal language with strong shell contrast, refined borders/shadows, and a tight display typography hierarchy. Older dark-theme stored values normalized to `Studio Ledger` on migration.
- **Theme overhaul** — Richer semantic token model in `app.css` covering shell chrome, surfaces, borders, shadows, table primitives, AKS toolbar/grid chrome, and interaction states. Light and dark themes share one token contract.
- **Shared shell and surface primitives** — Blank route-page header shell removed; pages now rely on the top bar for route identity and render a compact support strip for local pills and actions only.
- **Shared table system** — Table header, row density, hover/selection/sorting, truncation, and toolbar tokens defined and applied uniformly across feature areas.
- **Feature-area adoption** — Studio Ledger token language applied across: Service Bus (message-list toolbar, filter, rule-builder, save-dialog), AKS (namespace picker, confirm/port-forward dialogs, toolbar, side-panel controls), Storage (blob detail pane), Releases (detail/editor inline styling extracted to component CSS), Observability (logs surface and dialog styling hooks), Redis, Pipelines. Theme-unsafe accent/prod foregrounds and ad hoc overlay stack values replaced throughout.
- **E2E harness compatibility** — Legacy dark-theme alias normalization updated in the E2E helper so `Studio Ledger` maps cleanly.
- **Portima light theme gold emphasis** — Tinted neutral surfaces (bg, surface-2/3, borders, table headers, toolbars, AKS surfaces) shifted from green-tinted to amber-gold-tinted to match the brand identity. Green retained on shell chrome, accent, and nav active states.
- **Compile/test validation** — No source errors; app-component regression suite 46/46; E2E project build passed.

## Key decisions

- **Token-first overhaul** — All theme work driven through the semantic token model in `app.css` rather than page-by-page color tweaks. Single source of truth via `MainLayout` + `UserSettingsRepository`.
- **Pilot must differ in component form, not only palette** — First pilot pass was rejected because it read as color variants. Corrected pilot defined shell framing, pill/tab shape, dashboard cards, table chrome, and toolbar treatment differently between directions.
- **Studio Ledger as structural design system** — Chosen as the one set of component form rules. Future palettes vary color tokens only; typography, radii, shadows, and header treatment stay consistent across colorways.
- **Top bar owns route identity** — Blank per-page header shells removed. Pages render compact support strips for local pills/actions only. Keeps the shell entry pattern clean and non-redundant.
- **Tables treated as a shared primitive** — Single table contract defined before page migration to avoid per-page drift. Token-driven header, density, hover, selection, sort affordance, and truncation rules.
- **Preserve existing shell layout** — Global navigation geometry, route structure, and page-level composition kept intact to bound scope and avoid relearning cost.

## Validation performed

- Focused Windows MAUI build after theme-host and header-shell updates: passed.
- App-component regression suite: 46/46 passed.
- E2E project build: passed.
- Human visual spot-check in Studio Ledger and Portima light theme: approved by owner.

## Lessons learned

- Art direction pilots are only useful if they differ in component form. A palette-only comparison does not give enough signal to choose a direction — require visible differences in shell framing, pill/tab shape, and table chrome before asking for a decision.
- Inline style density in table-heavy areas (Storage, Service Bus, Release) blocks theme consistency and must be extracted to component-local CSS as a prerequisite, not an afterthought.
- Legacy theme alias normalization in test harnesses must be updated in the same change set as the theme-host migration, or E2E baseline assertions will fail.

## Follow-up

- Remaining palette variants (e.g. additional Studio Ledger colorways) can be added by varying color tokens only — component form is locked.
- Dashboard widget density and Observability chart surfaces were not fully adopted in this pass — candidates for a future visual polish slice.

## Archive note

> This file is present because the feature had **no Jira ticket** (Path B). Archive location: `docs/features/archive/visual-restyle-and-theme-overhaul/`.
