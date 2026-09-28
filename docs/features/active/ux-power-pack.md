# UX Power Pack — palette, pinned rail, env badge, keyboard grids, toast dedupe

State: In Progress

## Goal

Five daily-driver UX improvements. Analysis showed most infrastructure already exists —
these are extensions, not greenfield: `Ctrl+K` palette (`CommandPalette.tsx` +
`useCommandPaletteItems` + `fuzzyFilter`), a persisted pin model (`favoriteResources` +
`useTogglePinnedResource`), `AppConfig.IsProduction` (unwired in UI), roving-focus
precedent (`CollectionTree`), notification center (`NotificationSystem`).

## Scope

### 1. Command palette deepening
- Add SB queues/topics (per-namespace `useQueries` fan-out gated on `open`, mirroring
  `useServiceHealth`), AKS contexts (`useAksContexts`), saved SQL queries
  (`useSavedSqlQueries(null)` → all), pinned resources as items.
- New deep-link consumers: `state.context` in `AksWorkspaceContext`, `state.sql` in
  `SqlPage`, `state.compose` in `ServiceBusPage`. Prefer canonical search params where
  pages already read them (`/service-bus?ns=&entity=`).
- `type: "action"` items with `run` callback: Toggle demo mode, New API request,
  Run SQL query, New Service Bus message.
- Non-goal: per-connection SQL schema fan-out (too expensive) — saved queries cover it.

### 2. Pinned resources rail
- `PinnedRail` in `AppLayout` aside; items from `profile.config.favoriteResources`,
  navigate to `snapshot.resource.displayPath`.
- Pin buttons at resource surfaces (SB entity, Redis cache, storage account, AKS ns,
  SQL connection) building `FavoriteResource` snapshots.
- Commit to "displayPath is a complete URL"; add `?cache=` consumer to RedisPageContext
  (state-only today).

### 3. Environment badgeing
- `AppConfig.EnvironmentTag` (free-form `dev`/`stg`/`prd`) + fallback classifier
  (`isProduction` → PRD, else name heuristics). Named to avoid colliding with
  API Client request environments (`ApiEnvironment`).
- Settings → General: name + tag + wire the existing `isProduction` flag.
- Global pill in top bar (`env-badge`); PRD → destructive tint + `env-prd-banner` strip.
- Scope per-surface badges to identity-risk spots only (SQL writes badge, SB picker).

### 4. Keyboard-first data grids
- Shared `useGridKeyboardNav` hook: focused index, j/k+↑↓, `e`/`Enter` inspect,
  `/` focuses that grid's filter, optional g/G. Guard via
  `closest("input,textarea,select,[contenteditable],.cm-editor")` — the
  `document.body` guard breaks once rows are focusable.
- Ship order: `ResultsGrid` (SQL) → `BlobBrowserPanel` → `MessageList` (virtualized,
  `scrollToIndex` on focus) → `KeyBrowserPanel` (tree, h/l collapse/expand) →
  AKS `ResourceTable`.
- Update `KeyboardShortcutsPanel.KEYBOARD_SHORTCUTS` in the same change (repo rule).

### 5. Toast/notification dedupe
- `NotificationItem.count` + dedupe key `${type}|${title}|${body}`; same-key notify
  bumps count, refreshes timestamp, re-arms the 5s timer, renders `×N` in toast +
  history row.
- Skip dedupe when `action` present (Undo toasts must each live).
- Pure merge logic → `web/src/lib/notification-dedupe.ts` for vitest.

## Non-goals

- Per-connection SQL schema in the palette; resurrecting legacy `Environments`/
  `favoriteEntities` fields; per-surface env badges everywhere.

## Implementation tasks

- [x] `palette-items.ts` builder extraction + item extensions (SB fan-out, AKS ctx,
      saved queries, pins, actions) (commit d6280441)
- [x] deep-link consumers: Aks `state.context`, Sql `state.sql` (+ live `?connection=`
      retarget), SB `state.compose`, Redis `?cache=`, API Client `state.newRequest`
      (commit d6280441)
- [x] `PinnedRail` + per-surface pin buttons (SB entity, Redis cache, Storage
      account, AKS namespaces, SQL connection) (commit d6280441)
- [x] `EnvironmentTag` model + Settings UI + `EnvironmentBadge` + PRD banner
      (commit 16f6bfa6)
- [x] `useGridKeyboardNav` + grid integrations — ResultsGrid, MessageList,
      KeyBrowserPanel landed (commit d6280441); BlobBrowserPanel + AKS ResourceTable
      remain staged follow-ups (hook ready)
- [x] `notification-dedupe.ts` + NotificationSystem merge (commit 16f6bfa6)

## Test plan

- Vitest: `palette-items`, `notification-dedupe`, `env-badge` classifier, grid-nav
  index clamp/guards.
- Playwright: extend `layout.spec.ts` palette tests; new `pinned-rail`, `env-badge`,
  `grid-keys`, `notification-dedupe` specs (demo mode via `setDemoMode`).

## Sequencing (leverage/effort)

1. Toast dedupe (~1d, self-contained) → 2. Env badgeing (~2d) → 3. Pinned rail (~2-3d)
   → 4. Palette deepening (~3d) → 5. Keyboard grids (~4-5d, per-grid rollout).

## Decisions / risks

- Bare-key shortcuts suppressed via `closest(...)` guard, not `e.target === document.body`;
  bare `/` is free (`Shift+/` already opens shortcuts panel) — document it.
- Palette fan-out queries gated on `open`; `useAksNamespaces(false)` stays cache-only.
- Prefer canonical search params over `location.state` for new deep links (URL-shareable).
- `isProduction` gets its first UI — verify demo profile values so the badge reads right
  under demo mode.
