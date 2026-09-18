# Module 2 — Startup Warm-up & Resume Last Workspace

Make a cold launch land the operator back where they were, with the expensive
bootstrap data already in flight — triggered by **app startup** (AppLayout mount),
not by whichever page happens to load first.

## Changes

### `web/src/lib/hooks/useWorkspaceWarmup.ts` (new)

- Mounted once in `AppLayout`. After `profile` + `demo-mode` + `user-settings`
  resolve, honors the **existing** `warmupConnectionsOnStartup` toggle
  (the React app rendered it in General Settings but never consumed it —
  it previously only drove the MAUI app's `ConnectionWarmupService`).
- One-shot per session (ref guard); the queries' own `staleTime` (5 min) prevents repeats.
- Prefetches the first-entity topology nothing else fetches:
  - `aks-contexts` (the context picker list — no landing path ever fetches it),
  - `aks-namespaces` ctx-scoped (the ~18 s cold call, server-cached 5 min),
  - `aks-deployments` for the namespace the workspace will restore into
    (configured default, else persisted per-context pick, single concrete ns only),
  - `sb-queues` + `sb-topics` for the first Service Bus namespace.
- Deliberately skipped: per-area health probes (`aks-test`, `sb-test`, redis info,
  sql test, storage containers) — the footer status bar already fires those on
  every launch. Selection-dependent data (Redis SCANs, blob listings, pods) has
  no deterministic prefetch target.

### Resume last route — `AppLayout.tsx`

- `useState` lazy initializer captures `view-pref:last-route` **at mount** —
  before the save effect can overwrite it with `/` on this very launch.
- Restore effect (once, ref-guarded): when `user-settings` resolves, if still on
  `/` and `restoreLastWorkspaceOnStartup !== false` and the captured route
  differs → `navigate(captured, { replace: true })`. Any explicit navigation
  away from `/` before settings resolve means the user already chose — skip.
- Save effect on `location`: `saveViewPreference("last-route", pathname + search)`
  on every navigation, including `/` (the user's actual last page is the truth).

### Settings toggle — `GeneralSettings.tsx` + `UserSettings`

- New `UserSettings.RestoreLastWorkspaceOnStartup` (C#, default `true`) +
  `restoreLastWorkspaceOnStartup` in the TS model.
- "Restore last workspace on launch" checkbox in the Startup section
  (`restore-workspace-toggle`). Independent of the warmup toggle:
  restore controls *where you land*, warmup controls *what's already fetched*.

### Nav health dots

- Verified: `areaHealth` is already rendered per-area in the status bar
  (`status-bar-health-{id}`). No change needed — nav-icon dots would duplicate it.

## Acceptance criteria

- Launch landing on any non-dashboard page: `aks-contexts`, `aks-namespaces`,
  and `sb-queues`/`sb-topics` requests fire from AppLayout without visiting
  those pages; with warmup disabled they don't.
- With restore enabled: cold start at `/` redirects to the last route with its
  params; Module 1's namespace restore makes the AKS view immediately usable.
- Restore disabled: stays on Dashboard. Launching directly at a deep link
  (`/aks?…`) never redirects. Quitting from the Dashboard restores nothing
  (last route `/` is truthfully saved).
- Explicit click to Dashboard is never overridden after the initial restore.
