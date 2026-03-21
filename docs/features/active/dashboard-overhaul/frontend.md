# Frontend Plan — Dashboard Overhaul

## Affected files

- `src/SwebKit.App/Components/Pages/DashboardPage.razor` — full rewrite
- `src/SwebKit.App/Components/Pages/DashboardPage.razor.css` — new scoped styles
- `src/SwebKit.App/Components/Shared/HealthTile.razor` — new reusable tile component
- `src/SwebKit.App/Components/Shared/HealthTile.razor.css`

## Component breakdown

### `HealthTile.razor`

Reusable tile component. Parameters:
- `string Title` — feature area name
- `string Icon` — Fluent icon name (once icons feature is complete; emoji interim)
- `HealthTileData? Data` — nullable; null = loading state
- `bool IsConfigured` — false = show unconfigured callout
- `string? Error` — non-null = show error state

States:
1. **Loading** — `FluentProgressRing` centred in tile
2. **Not configured** — muted text + "Configure in Settings" link
3. **Error** — `--color-error` accent + error summary text
4. **Healthy** — primary metric value prominent, secondary label below
5. **Warning** — `--color-warning` accent when metric > 0 (e.g. DLQ > 0, unhealthy pods)

### `DashboardPage.razor` — rewrite plan

**Section 1: Health tiles grid** (2×3 or 3×2 grid)
- Fetches health data in parallel using `Task.WhenAll` on page init
- Each area calls its client's lightest read method:
  - Service Bus: sum of DLQ message count across all watched namespaces (`PeekDeadLetterAsync` with `maxMessages: 0` for count, or a dedicated count method if available)
  - AKS: `GetPodsAsync` → count where status ≠ Running/Completed/Succeeded
  - Redis: `GetKeysAsync` with scan → count where TTL < 300 seconds
  - Releases: `GetReleasesAsync` → count pending approvals
- If client is not configured (no connection), tile shows unconfigured state immediately (no fetch attempt)
- Auto-refresh: `PeriodicTimer` at 60-second interval; cancels on page disposal

**Section 2: Recent activity feed**
- Subscribe to `IAppEventBus` for relevant events (a new `ActivityEvent` wrapper, or map existing events)
- Hold up to 10 most recent `ActivityRecord` entries in a local list (session-only, not persisted)
- Each record: icon, description, timestamp (relative: "2 minutes ago")
- Empty state: "No recent activity — actions you take will appear here"

**Section 3: Pinned quick-access**
- Read `AppState.PinnedEntities` (or equivalent from `AppStateService`)
- Render as a compact horizontal chip row
- Each chip: icon + entity name; click fires `NavigateToAreaEvent` targeting the entity
- Empty state: "No pinned items — pin queues and pods from Settings"

## CSS notes

- Health tiles grid: `display: grid; grid-template-columns: repeat(3, 1fr); gap: var(--spacing-md)` — collapses to 2 or 1 column on narrower window sizes
- Tile card: `background: var(--color-surface); border: 1px solid var(--color-border); border-radius: 6px; padding: var(--spacing-lg)`
- Warning tile: left border accent `4px solid var(--color-warning)`
- Error tile: left border accent `4px solid var(--color-error)`

## Tasks

- [ ] Create `HealthTile.razor` with all 5 states
- [ ] Rewrite `DashboardPage.razor` with 3 sections
- [ ] Wire parallel health fetch with `Task.WhenAll` and per-tile error isolation
- [ ] Implement activity feed with event bus subscription
- [ ] Implement pinned quick-access section
- [ ] Add 60-second auto-refresh with `PeriodicTimer`
- [ ] Write scoped CSS for grid and tile states
