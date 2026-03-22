# Feature Archive — Dashboard Overhaul

---

title: "Dashboard Overhaul"
status: "Archived"
completed: "2026-03-22"

---

## Goal

Transform the home dashboard from a static link grid into a meaningful landing page that surfaces health signals, recent activity, and quick-access pinned items at a glance.

## What was built

- **Health summary tiles** (`HealthTile.razor`) — one tile per area (Service Bus, AKS, Redis, Releases), each with 5 states: loading, not configured, error, healthy (value=0), warning (value>0). Left-border accent distinguishes warning/error/ok.
- **Parallel health fetch** — `Task.WhenAll` with a 10-second timeout; each tile catches its own errors independently.
- **60-second auto-refresh** — `PeriodicTimer` cancelled on page dispose (BL-7 pattern).
- **Activity feed** — subscribes to the new `ActivityEvent` event bus record; holds up to 10 session-only entries.
- **Pinned quick-access** — renders `AppState.Config.FavoriteEntities` as a chip row; click fires `NavigateToAreaEvent`.
- **Dashboard nav item** — added Home icon entry to `LeftNav.razor`; dashboard reachable via `/` and `/dashboard`.

## Key files

| File | Role |
|---|---|
| `src/SwebKit.App/Components/Pages/DashboardPage.razor` | Rewritten page — 3 sections |
| `src/SwebKit.App/Components/Shared/HealthTile.razor` | Reusable health tile component |
| `src/SwebKit.App/Models/DashboardModels.cs` | `HealthTileData` record |
| `src/SwebKit.Core/Services/AppEventBus.cs` | Added `ActivityEvent` record |
| `src/SwebKit.App/Components/Layout/LeftNav.razor` | Added Dashboard nav item |

## Decisions

**D1 — Fire-and-forget health fetch on init**
`OnInitializedAsync` returns `Task.CompletedTask` immediately and fires `LoadHealthDataAsync` without awaiting. This lets the page render instantly in loading state. Tiles update as each fetch completes. Alternative (awaiting in init) would block the first render.

**D2 — 10-second per-cycle timeout**
Each 60-second refresh cycle wraps all fetches in a linked `CancellationTokenSource` with a 10-second `CancelAfter`. Prevents a slow or hung client from blocking the next cycle.

**D3 — Redis health: first-page TTL scan (max 100 keys)**
Scanning all keys and checking TTL for each would be O(n) and too slow for a dashboard tile. Limited to the first scan page (100 keys). Trade-off: result is a sample, not exact count.

**D4 — Activity feed is session-only**
`ActivityEvent` is published to `IAppEventBus` and held in a `List<ActivityRecord>` on the page. Not persisted. Intentional per scope; persistent history is a separate future feature.

**D5 — Dual route `/` and `/dashboard`**
Added `@page "/dashboard"` alongside `@page "/"` so `NavigateTo("dashboard")` resolves correctly through the existing `MainLayout` routing pattern without special-casing.

## Follow-up items

- Wire `ActivityEvent` publishing in other pages (ServiceBus, AKS, Redis, Releases) to populate the activity feed with real user actions.
- Redis health tile scans only the first 100 keys — consider a smarter approach (e.g. server-side `SCAN` with TTL filter) if accuracy matters.
- Manual verification of the 60-second auto-refresh and per-tile error isolation has not been formally recorded.
