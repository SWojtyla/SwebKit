# Module 2 — Startup Warm-up & Resume Last Workspace

Make a cold launch land the operator back where they were, with the expensive AKS bootstrap
already in flight.

## Changes

### `web/src/lib/hooks/useWorkspaceWarmup.ts` (new)

- Mounted once in `AppLayout`. After `profile` + `demo-mode` resolve:
  - `prefetchQuery` `aks-contexts`, `aks-namespaces` (ctx-scoped key from Module 1), `aks-test`.
  - Skip when AKS unconfigured and not demo.
  - One-shot per session (ref guard); the queries' own `staleTime` (5min, Module 1) prevents repeats.
- Optional stretch: prefetch `aks-deployments` for the restored namespace.

### Resume last route — `AppLayout.tsx`

- Effect on `location`: `saveViewPreference("last-route", location.pathname + location.search)`
  on every navigation.
- Mount-time effect: if `location.pathname === "/"` and stored route exists and differs →
  `navigate(stored, { replace: true })`. Runs once (ref guard), never after explicit navigation.

### Settings toggle — `GeneralSettings.tsx` + `UserSettings`

- "Restore last workspace on launch" (default on) persisted in `user-settings.json`
  (`UserSettings` model, `useUpdateUserSettings`). Restore honors the flag; warm-up always runs.

### Nav health dots (verify)

- `areaHealth` is already computed in `AppLayout` — check where it's rendered; if not per-nav-item,
  add a compact status dot per nav icon.

## Acceptance criteria

- Fresh launch with AKS configured: `aks-namespaces` request fires before `/aks` is visited;
  first AKS visit shows the list in <2s (server 5-min cache already warm).
- With restore enabled: cold start lands on the last page with its params; Module 1's namespace
  restore makes the AKS view immediately usable.
- Restore disabled: always opens Dashboard.
- Explicit click to Dashboard is never overridden after the initial restore.
