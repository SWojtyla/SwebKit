# Summary — UX Polish (archived 2026-09-18)

## Goal

Make SwebKit feel instant and intuitive: eliminate stale-data windows and silent
failures, restore the operator's last workspace (context, namespace, page,
selections), and normalize loading/empty/error patterns across every feature page.

## Delivered

- **AKS context switching** — fixed 10 confirmed defects: the pooled AKS client
  now receives the explicit context (was: profile context under the requested
  key); `POST /api/aks/context` tests the target before persisting and leaves the
  saved profile untouched on failure; profile saves only evict pooled clients when
  connection-relevant config actually changed; all AKS query keys are
  context-scoped with a `namespaceToken` gate during switches; per-context
  namespace restore; MRU `ContextSelector`/`NamespaceSelector` with full
  keyboard/aria support.
- **Startup warm-up & resume** — `useWorkspaceWarmup` prefetches AKS bootstrap
  queries and Service Bus lists at app startup (honoring the pre-existing warmup
  toggle); `view-pref:last-route` restores the last page via a new
  `RestoreLastWorkspaceOnStartup` setting.
- **Page restore & deep-link parity** — shared `useUpdateSearchParams` hook; URL
  params drive selection on Storage (`?account/?container/?prefix/?blob/?view`),
  Redis (`?tab=` + per-cache pattern), SQL (`?connection`), Monitoring (`?tab=`);
  settings CTAs on empty states.
- **Consistency sweep** — shared `SearchableSelect` component adopted by Redis,
  Storage, SQL, Service Bus, and context selectors; every `queryFn` consumes
  `{ signal }`; silent mutations wired to `useNotifyMutation`; all disabled
  controls carry a `title` reason; `QueryState` normalization.

## Key decisions

- Query keys must carry the server-side identity (context) — a key that omits it
  serves one backend's data under another's name. Recorded in
  `docs/pitfalls/react-frontend.md`.
- Pooled-client cache key and factory argument must describe the same target
  (`docs/pitfalls/dotnet-csharp.md` CS-10).
- Restore-on-launch reads must happen before the save effect's first write —
  captured lazily at mount.
- `view-pref:` localStorage convention is the persistence mechanism for all
  restore state.

## Validation

- `dotnet build` sidecar + app: 0 warnings/errors; `SwebKit.Sidecar.Tests`
  466/466; `SwebKit.Core.Tests` 1000/1000.
- `tsc --noEmit` clean; `vitest run` 473/473.
- Playwright full suite 345/345 (incl. 24 new specs across context-switching,
  workspace-resume, page-restore, searchable-select).

## Lessons learned

- Scope TanStack Query keys by every server-side discriminator from day one —
  retrofitting touched ~20 hooks.
- A "test before persist" boundary on profile/context switches prevents a whole
  class of stale-connection bugs.

## Follow-up

- Stretch items remain unscheduled: command-palette context switching, namespace
  MRU badges, shared server-side namespace cache, jump-to-resource.
