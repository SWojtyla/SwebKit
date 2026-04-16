# Decisions - visual-restyle-and-theme-overhaul

---

title: "Decisions - visual-restyle-and-theme-overhaul"
owner: "GitHub Copilot"
status: "Active"

---

## Decision 001 - Preserve the existing shell layout

**Status:** Accepted

**Date:** 2026-04-15

### Context

The user wants the app to look more polished and pleasant, but explicitly does not want the global layout changed.

### Decision

Keep the existing shell geometry, navigation placement, route structure, and page-level composition. The feature will focus on visual system quality: themes, typography, spacing, surfaces, tables, and interaction states.

### Consequences

- Reduces relearning cost for existing users.
- Keeps the work bounded and easier to phase.
- Some layout proportions that are merely acceptable, not ideal, will remain until a separate layout-focused feature exists.

### Alternatives considered

- Redesign the shell layout now - rejected because it expands scope beyond the user's request.
- Make isolated page-level layout changes during polish - rejected because that would create inconsistent geometry across the app.

---

## Decision 002 - Use a token-first theme overhaul

**Status:** Accepted

**Date:** 2026-04-15

### Context

Theme state is already centralized through `MainLayout`, `app.css`, and `UserSettingsRepository`, but many pages bypass the design system with inline styles or one-off surface rules.

### Decision

Drive the overhaul through a richer semantic token model in `src/SwebKit.App/wwwroot/app.css`, keeping `MainLayout` theme state and persisted user settings as the single source of truth. Page and component styling should consume shared tokens; inline color and surface styles should be retired where they block consistency.

### Consequences

- Theme quality improves across the app instead of only on selected pages.
- Shared shell and page primitives become easier to maintain.
- Early implementation work must spend time on token design before page adoption accelerates.

### Alternatives considered

- Tweak colors page by page without changing the token model - rejected because it does not scale and would preserve inconsistency.
- Introduce a second competing theme source - rejected because it would split responsibility between CSS and component state.

---

## Decision 003 - Treat tables as a shared product primitive

**Status:** Accepted

**Date:** 2026-04-15

### Context

The request explicitly calls out tables and column headers as a priority. Current table implementations are fragmented: some use limited global helpers, some use page-specific grid CSS, and some use inline `<table>` styling.

### Decision

Create one shared table contract before broad page migration. The contract should define header styling, row density, hover/selection/focus states, truncation and wrapping rules, sorting/filter affordances, and sticky behavior where appropriate.

### Consequences

- Table-heavy pages can converge on one visual language instead of solving the problem repeatedly.
- Migration becomes more predictable and reviewable.
- A short upfront foundation pass is required before per-page polish begins.

### Alternatives considered

- Polish each table independently - rejected because it would preserve drift.
- Limit the work to colors only - rejected because the request includes header and table treatment, not just palette changes.

---

## Decision 004 - Final theme catalog will be decided after the token audit

**Status:** Proposed

**Date:** 2026-04-15

### Context

The app currently ships with one dark theme and several named light themes. It is not yet clear whether all current variants should be kept, merged, or replaced after the broader token overhaul.

### Decision

Do not lock the final number of themes during planning. Complete the token audit first, then decide whether the existing theme catalog should be refined in place or reduced to a smaller curated set of polished themes.

### Consequences

- Avoids committing to visual options before contrast and consistency are evaluated.
- Leaves room to simplify maintenance if the current theme set proves redundant.

### Alternatives considered

- Freeze the existing theme catalog as-is - rejected because the current plan is specifically about theme overhaul.
- Cut themes immediately during planning - rejected because the audit has not happened yet.

---

## Decision 005 - Validate the art direction with an in-app pilot before full rollout

**Status:** Accepted

**Date:** 2026-04-15

### Context

The visual direction is easier to judge in real usage than from prose alone, but implementing two full app-wide overhauls before choosing would be wasteful.

### Decision

Build a low-cost live pilot that compares two candidate dark directions before full rollout. The pilot is intentionally limited to the theme host, Settings appearance selection, shell chrome, dashboard surfaces, and one real table-heavy workflow in Storage.

### Consequences

- The direction can be chosen from a live operator workflow instead of a static mockup.
- The comparison stays cheap because it avoids a full dual rollout.
- The chosen pilot slice should still be implemented using the same token-first architecture intended for the final restyle.

### Alternatives considered

- Choose the direction from written descriptions only - rejected because the differences in polish and readability would be too abstract.
- Fully implement two complete app-wide theme overhauls - rejected because it would cost too much before a direction is chosen.

---

## Decision 006 - Pilot candidates must differ in component form, not only palette

**Status:** Accepted

**Date:** 2026-04-15

### Context

The first pilot pass established new tokens but still read primarily as alternate themes instead of alternate visual systems. That is not enough to let the user choose a real direction.

### Decision

Refit the pilot around two new candidates: `Command Deck` and `Studio Ledger`. The comparison must change component form as well as palette: shell framing, pill and tab shape, page-header treatment, dashboard cards, Storage table treatment, and AKS toolbar/grid chrome should all differ materially between the two directions.

### Consequences

- The pilot becomes a stronger design decision tool instead of a color review.
- More pilot code is required up front in shared UI primitives and AKS-specific styling.
- The resulting token and component work is still reusable for the full rollout.

### Alternatives considered

- Keep the original pilot and ask the user to choose anyway - rejected because the user explicitly called out that the current result does not feel like two distinct designs.
- Expand directly into a full restyle without a corrected pilot - rejected because the direction would still be under-specified.

---

## Decision 007 - Studio Ledger is the global shell direction

**Status:** Accepted

**Date:** 2026-04-16

### Context

The pilot comparison is no longer needed. The user chose `Studio Ledger` as the app-wide direction after the premium slate-metal refinement.

### Decision

Use `Studio Ledger` as the global dark design language for the shell, shared surfaces, and future page adoption work. Retire the `Command Deck` pilot from the active theme catalog and treat older dark theme values as legacy aliases that normalize to `Studio Ledger`.

### Consequences

- The rollout can move from art-direction comparison into shared-primitives and page adoption work.
- The theme selector and persisted theme handling should present `Studio Ledger` as the default dark option rather than a pilot.
- Legacy stored dark-theme values need compatibility mapping so existing users are migrated cleanly.

### Alternatives considered

- Keep both pilot directions exposed indefinitely - rejected because the user selected one direction and the comparison UI would become stale.
- Revert to the pre-pilot dark theme as default - rejected because it would discard the chosen visual direction.

---

## Decision 008 - Future palettes should branch from the Studio Ledger structure

**Status:** Accepted

**Date:** 2026-04-16

### Context

The user wants multiple color palettes, but only after the structural language is settled. The risk is reintroducing competing visual systems instead of palette variants.

### Decision

Keep `Studio Ledger` as one structural design system and let future palettes vary color tokens, not component form. Typography hierarchy, radii, shadows, header treatment, and toolbar/table framing should stay consistent across those palettes.

### Consequences

- Additional palettes can be added later without reopening the shell redesign.
- The CSS token model should keep the selected `Studio Ledger` language easy to reuse for alternate colorways.
- Palette experimentation becomes bounded: colors can evolve, but the overall product identity stays coherent.

### Alternatives considered

- Treat each palette as a separate visual direction - rejected because it would recreate the same drift the pilot was meant to resolve.
- Freeze one single palette forever - rejected because the user explicitly wants room for multiple palettes.

---

## Decision 009 - The top bar owns route identity; pages only add compact support content

**Status:** Accepted

**Date:** 2026-04-16

### Context

Once `Studio Ledger` became the chosen direction, the old route-page header shell no longer had a justified role on every page. It duplicated the route identity already rendered in the top bar and produced empty or oversized entry surfaces.

### Decision

Use the shell top bar as the primary source of route identity for all pages. Routed pages should only render a compact support strip when they need local pills or actions such as scope, counts, warnings, or settings links. Full in-body page headers should be reserved for explicit exceptions.

### Consequences

- The shell entry experience becomes cleaner and less repetitive.
- Page-local actions remain available without reintroducing a second title bar.
- Future shell and palette work can refine one shared entry pattern instead of several competing ones.

### Alternatives considered

- Keep the old route-page header shell on every page - rejected because it duplicated the top bar and left blank surfaces behind.
- Remove all page-entry support content entirely - rejected because pages still need local pills and actions in several workflows.
