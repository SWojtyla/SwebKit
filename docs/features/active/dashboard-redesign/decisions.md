# Decisions — Dashboard Redesign

## DEC-DR-1 — Calm & minimal default, density opt-in

**Decision:** The default dashboard ships with a small quiet tile set (health summary, favorites,
recents). All other tiles (KPI, activity, open tabs, watch tiles) remain available but hidden by
default, added via the builder panel.

**Why:** User explicitly wants "calm and minimal by default but able to add extra panels." The
customization model carries the density; the default carries readability.

**Consequence:** Empty/quiet states become first-class design work; operational users must add
their panels once (accepted, see DEC-DR-2).

## DEC-DR-2 — Clean preference reset, no layout migration

**Decision:** The new default layout replaces old dashboard tile preferences. No migration of
visibility/order/size from the command-center era. Old `ui-state.json` payloads must still load
safely through the existing `NormalizeDashboardPreferences` safe-drop path (no crash), but users
start from the new defaults.

**Why:** User accepted a clean reset; migration of a fully changed visual model has poor
cost/benefit.

**Consequence:** Core tests must cover old-payload inputs against the new defaults to prove the
no-crash guarantee. Saved views structure is preserved (views themselves are not deleted —
only re-normalized against the new registry/defaults).

## DEC-DR-3 — Dashboard design tokens in a dedicated stylesheet, components keep CSS isolation

**Decision:** Shared visual values (spacing scale, type scale, muted palette, area accent
variables, shadow token) live in one dashboard token stylesheet exposed as CSS custom properties;
each component still owns its scoped `.razor.css` and consumes the tokens.

**Why:** Architecture note requires component-local CSS isolation; tokens avoid copy-paste drift
across the six dashboard components without parent-page style leakage.

**Consequence:** Token file location (global `wwwroot/css` import vs. `::deep` wrapper on the
page) is settled at Wave B start; global import is the current lean.

## DEC-DR-4 — Builder panel over drag-and-drop

**Decision:** Customization stays panel-driven (template gallery + layout list + hidden section +
view controls). No drag-and-drop grid.

**Why:** User chose "Improved builder panel"; drag-and-drop in MAUI Blazor Hybrid WebView adds
interaction/testing complexity disproportionate to the benefit.

## DEC-DR-5 — Decomposition before redesign, refresh architecture frozen

**Decision:** Wave A (mechanical partial-class decomposition) lands and validates before any
visual work. The refresh/render engine (semaphore gate, per-tile budgets, render coalescing,
snapshot cache) is out of scope for Waves B–D — presentation changes only.

**Why:** Proven pattern from `api-client-page-decomposition`; keeps every redesign diff reviewable
and regressions bisectable.

## DEC-DR-6 — Builder redesigned in place, not extracted to a component

**Decision:** The redesigned builder panel (view controls + template gallery + current-layout list
+ hidden section) stays inline in `DashboardPage.razor` rather than becoming a standalone
`DashboardBuilderPanel.razor` as `frontend.md` suggested.

**Why:** The builder is deeply coupled to ~30 page fields (`_newServiceBus*`, `_editAks*`,
`_isCustomizing`, `_editingTileId`, …) and many two-way `@bind` inputs. Extraction would require a
large, fragile parameter/callback surface or an `@ref` back-reference for little benefit.
`frontend.md` explicitly allowed "heavily simplified" as an alternative to extraction.

**Consequence:** The page markup is larger, but the builder shares page state directly with no
plumbing. If the builder grows further, revisit extraction with an explicit view-model.
