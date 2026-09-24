# SwebKit — Global Context

One-page orientation. Read this first for any non-trivial task; follow the links at the bottom only for the areas you actually touch.

## Stack

- **Desktop shell**: Tauri (Rust) — `src-tauri/`
- **Frontend**: React 19 + TypeScript + Vite — `web/` (TanStack Query, Tailwind, CodeMirror)
- **Backend**: ASP.NET minimal-API sidecar — `src-sidecar/` (REST under `/api/*`, spawned by the shell)
- **Shared .NET libs**: `src/SwebKit.*` (Core, Azure, Kubernetes, Redis, Sql, Agents, DevOps, Observability)
- **Tests**: `tests/SwebKit.<Area>.Tests` (xUnit) · `web/src/**/*.test.ts` (vitest) · `web/e2e/` (Playwright, demo mode)

## Directory map

| Path | Contents |
| ---- | -------- |
| `web/src/components/<domain>/` | Feature UIs: service-bus, aks, api-client, storage, redis, sql, monitoring, agent, settings, dashboard |
| `web/src/components/shared/`, `ui/` | Shared controls: ConfirmBar, Dialog, EmptyState, SidePanel/ResizablePanel, NotificationSystem |
| `web/src/lib/hooks/` | TanStack Query hooks per domain (`useServiceBus.ts`, …) |
| `web/src/lib/` | Utilities, `types.ts`, `api.ts` (`apiFetch`/`apiSend`), `codemirror-theme.ts` |
| `src-sidecar/Endpoints/` | Minimal API route handlers per domain |
| `src/SwebKit.Core/` | Domain models, abstractions (`IServiceBusClient`, …), demo clients |
| `src/SwebKit.<Area>/` | SDK-backed implementations (`AzureServiceBusClient`, …) |
| `docs/architecture/` | System map, design flows, codebase guide, per-functionality deep dives |
| `docs/pitfalls/` | Hard-won traps per stack — read the relevant file BEFORE non-trivial changes |
| `docs/features/active/` | One `<slug>.md` plan file per in-flight feature |

## Conventions

- **Entity paths in routes**: `encodeURIComponent` — subscription paths contain `/`.
- **Mutations**: through hooks in `web/src/lib/hooks/`; always `useNotification()` success/error feedback; invalidate the domain's base query keys (never prefix-match).
- **Destructive actions**: `ConfirmBar` confirmation, always.
- **Testids**: `data-testid` on every state-changing control — the e2e suite depends on them.
- **CodeMirror**: `swebkitHighlighting()` (never `defaultHighlightStyle` — light-only); hidden mirror `<textarea>` so Playwright/AT sees the full document.
- **Azure SDK**: clients via `IServiceBusConnectionPool` / per-domain pools — never construct SDK clients in a request handler; pass `CancellationToken` through.
- **Demo mode**: `DemoModeService` + `Demo*Client` classes in Core — every feature must stay meaningful in demo mode.
- **Sidecar errors**: never leak connection strings, SAS keys, or raw SDK exception text.
- **URL state**: selection/filters live in search params (deep-linkable); batch `searchParams` updates — snapshots go stale.

## Commands

```bash
cd web && npm run build                        # tsc + vite
cd web && npx vitest run                       # unit tests
cd web && npx playwright test                  # e2e (demo mode; scope to a spec for iteration)
dotnet build src-sidecar
dotnet test tests/SwebKit.Sidecar.Tests        # per-area: tests/SwebKit.<Area>.Tests
```

## Feature workflow

1. Plan: `docs/features/active/<slug>.md` — one file: `State`, Goal, Scope/Non-goals, Decisions, Tasks, Test plan, Validation. It's the shared intent record across tools/sessions — write it even if the planning happened in a native plan mode. Skills: `swebiplan` (plan only), `swebify` (autonomous end-to-end).
2. `State:` is one of `Proposed`, `Planned`, `In Progress`, `Review`, `Done`.
3. Close-out: fold durable learnings into `docs/pitfalls/` or `docs/architecture/`, delete the plan file, drop its line from `docs/features/README.md`. Git history is the archive.

## Deeper reading — route on demand

- `docs/architecture/index.md` — task → doc router
- `docs/pitfalls/index.md` — trap catalog
- `docs/features/README.md` — active feature catalog
- `docs/security/aikido-mcp-scan.md` — security scan rules
