# AKS navigator rail — collapsible sections

State: Review

## Goal

Replace the grouped dropdown menus (`aks-topbar-nav.md`) with the
`docs/prototypes/aks/rail.html` navigator rail — a left sidebar where the four
task groups are always visible. Unlike the prototype, **group sections are
collapsible** (the prototype's headers are static labels): that was the explicit
ask.

## Why the rail over the dropdowns

The dropdown menus decluttered the strip but kept every view two clicks away and
hid the grouping the moment the menu closed. The rail keeps all 18 views one
click away, makes the grouping a permanent part of the page, and matches how the
prototype's own comparison framed it (the rail was the runner-up layout, picked
now that collapsible sections fix its one downside).

## Scope

- `AksNavRail.tsx` inside the `aks-content` flex row, replacing `AksNavBar`
  (deleted).
- Section headers `aks-nav-group-<id>` toggle their item list; `aria-expanded`
  reflects the section state.
- Collapsed-section set persists in `localStorage`
  (`aks-nav-rail-collapsed-groups`), minimized rail in `aks-nav-rail-minimized`.
- Render-time adjustment: when `activeTab` changes to an item inside a collapsed
  section (deep link, in-table cross-link), that section re-expands — the tab
  must be visible. Only fires on actual tab change, so collapsing the active
  tab's own section stays possible.
- Minimized mode (~icon strip): section headers collapse to hairlines, items
  show glyph + tooltip; collapse state is ignored but preserved.
- Filter input narrows items across groups (ignores collapse while filtering);
  Enter opens the first match.
- `aks-nav-current` footer keeps the `Group › Item` anchor for specs and for
  when the active item is inside a collapsed section.
- Testid contract unchanged: `aks-tabs`, `aks-nav-group-<id>` (+`aria-expanded`),
  `aks-nav-menu-<id>`, `aks-tab-<id>`, `aks-nav-current` — `openAksTab` in
  `e2e/helpers.ts` keeps working untouched.
- `openNavGroup`/`setOpenNavGroup` removed from `AksNavValue` — the dropdown
  state is dead.

## Same deliberate deviation as the dropdowns

No per-item count badges: only the pods list is fetched at page level, so 17
badges would cost 17 more queries for decoration.

## Also on this branch: linked-root Git grant fix

Unrelated bug bundled per request. A linked API root's Git badge (sidecar:
`git rev-parse --show-toplevel`, no gate) could say `dev` while the Git drawer
claimed the path "is not a Git repository". The drawer's `gitIsRepo` goes
through the Tauri `AllowedRoots` grant list — which only holds folders the user
picked via an OS dialog — and `is_repo_impl` swallows the denial as `false`.
Now `GitPanel.refresh` checks `restoreAllowedRoot`'s result first: an
unauthorized path gets a dedicated `not-authorized` state naming the real
problem plus an "Authorize this folder…" button (`pick_directory` = the only
legitimate grant channel). A genuinely non-repo granted path still lands on
`not-a-repo`.

## Test plan

- Unit: `loadNavCollapsedGroups`/`saveNavCollapsedGroups` round-trip, stale-id
  drop, corrupt pref (3 tests in `aks-workspace-context.test.ts`).
- e2e (`aks.spec.ts` "navigator rail" test): sections visible + active chip,
  item click navigates, collapse hides items while footer keeps the anchor,
  re-expand, filter + Enter, minimize/restore.
- Existing suites: every `openAksTab` call site exercises the rail's
  group→item choreography.

## Validation results

- [x] aks.spec.ts — 14/14 (new rail test covers collapse, filter, minimize, current chip)
- [x] aks-ux / aks-url-state / aks-multi-context / aks-portforward-analysis / aks-context-switch / aks-deferred — 38/38 via the untouched `openAksTab` helper
- [x] vitest — `aks-workspace-context.test.ts` 25/25 (3 new pref tests)
- [x] tsc + eslint — clean (only a pre-existing `GitPanel` memoization warning)
- [x] Aikido — 0 findings on the 8 touched files
