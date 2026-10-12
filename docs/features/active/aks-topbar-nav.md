# AKS top-bar declutter — grouped menus

State: Review

## Goal

The AKS page crammed 18 resource views into one horizontal strip — 8 direct
tabs, 6 hidden behind a "Network ▾" second-row submenu, 4 trailing — on top of
a scope toolbar that already wraps at 1280px. Replaced with four task-grouped
dropdown menus (prototype `docs/prototypes/aks/groups.html`, concept A, chosen
by the user over the rail and breadcrumb+switcher concepts).

## What shipped

- `navGroups` in `aks-workspace-context.ts` is the single source of truth for
  grouping, labels, glyphs and one-line descriptions; `allTabs`/`TabId`/`parseTab`
  are derived from it, so `?tab=` deep links keep working unchanged.
- `AksNavBar.tsx` renders the 4 group buttons + absolutely positioned dropdown
  panels — they float over content instead of pushing it like the old Network
  submenu row. Active group is underlined; active item is highlighted; a
  `Group › View` chip on the right keeps "where am I" legible while menus hide
  inactive views.
- `openNavGroup: NavGroupId | null` in `AksNavValue` replaced the boolean
  `networkMenuOpen`. Esc and outside-click dismiss; only one menu open at a time.
- Testids: `aks-nav-group-<id>`, `aks-nav-menu-<id>`, `aks-nav-current`; menu
  items keep the existing `aks-tab-<id>` testids so deep links, docs, and any
  muscle memory around the old ids survive.
- e2e `openAksTab(page, id)` helper in `helpers.ts` performs the
  group→item choreography for the ~50 spec call sites that used to click a
  flat tab.

## Deliberate deviation from the prototype

Per-item count badges were dropped: only the pods list is fetched at page
level (`useAksQueries`), so a count on every item would mean lifting 17 more
queries into the page just to decorate a menu. Revisit if resource counts are
ever lifted for another reason.

## Non-goals

- Refresh controls untouched (prototype B/C's `⟳ 10s ▾` fold was not adopted).
- Detail panels, log views, shell — untouched.
- No keyboard arrow-key menu nav (prototype had none; items stay
  Tab-focusable).

## Test plan — results

- New e2e `grouped nav menus open, track the active view, and dismiss`:
  group buttons render, menu opens under the group, deep HTTPRoutes view is
  one click away, `aks-nav-current` shows `Network › HTTPRoutes`, item click
  closes the menu, Esc closes without switching.
- `npm run build` + `npx vitest run` (902) green.
- Playwright: `aks.spec.ts`, `aks-url-state`, `aks-deferred`,
  `aks-multi-context`, `aks-portforward-analysis`, `aks-ux`,
  `contextual-assistant`, `global-agent-panel` — 68 tests green after
  migrating all `aks-tab-*` clicks to `openAksTab`.
