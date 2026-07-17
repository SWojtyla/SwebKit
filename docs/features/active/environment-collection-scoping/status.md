# Status — Environment Collection Scoping

## Current State

`Review`

## Quick Summary

Environments can now be scoped to a collection (or left global). The picker shows the active
collection's environments plus global ones; the active environment is remembered per collection; and
Bruno-imported environments are scoped to their originating collection. Existing environments stay
global (non-destructive).

**Jira:** not linked

## Progress Checklist

- [x] Phase 1 — Local scoping: model (`CollectionId`, `ActiveEnvironmentIdByCollection`), repository
      (`AddEnvironmentAsync(collectionId)`, `SetActiveEnvironmentForCollectionAsync`, delete cleanup),
      per-collection active-env resolution, toolbar picker filter + grouping, editor scope selector,
      Bruno import scoping (local) + per-scope dedupe. Tests + build green.
- [x] Phase 2 — Linked scoping: per-collection `environments/` folder read/write, `environments/`
      excluded from the tree, scoped import routing, `WriteCollectionToLinkedRootAsync` returns dir.
      Tests + build green.
- [x] Phase 3 — Local scope editing works via the editor select (edit-clone `CollectionId` bug fixed).
      Linked scope-move **deferred** (no UI consumer — see index.md Out of scope).

## Validation

- Automated: `SwebKit.Core.Tests` — `EnvironmentRepositoryTests`/`BrunoFolderImportTests` (23) and
  `LinkedCollectionRootTests` (36) green, including new scoping tests. `SwebKit.Core` + `SwebKit.App`
  build with 0 errors.
- Backward compatibility: existing local/linked environments have no scope → load as global; existing
  `ActiveEnvironmentId` honored as fallback. No schema bump (additive nullable field + additive dict).

## Not yet done (needs the user)

- Manual in-app pass (MAUI Blazor Hybrid can't be driven headlessly here): import the BOA workspace into
  a clean `.swebkit-api`; confirm each collection's picker shows its own `DEV`/`STG`/… plus globals;
  switch collections and confirm the env list + active selection follow; create a collection-scoped and
  a global env; reload and confirm scope + per-collection active selection persist.
- No bUnit test for the toolbar picker filtering (no toolbar bUnit harness exists today); covered by
  manual verification. Core scoping logic is unit-tested.

## Blockers

_None._

## Notes

- Changes are in the working tree, **not committed** — the user commits.
