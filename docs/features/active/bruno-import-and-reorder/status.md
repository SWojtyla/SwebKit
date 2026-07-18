# Status — Bruno Multi-Collection Import & Persisted Tree Reorder

## Current State

`Review`

## Quick Summary

Fixes three API-client issues surfaced by importing a large Bruno workspace into a linked git repo:
flatten multi-collection imports (drop the wrapper), import per-collection environments/variables,
and persist drag-and-drop reorder for linked collections.

**Jira:** not linked

## Progress Checklist

- [x] Flatten: `BrunoFolderImporter` imports each child collection separately for a workspace folder
- [x] Environments: per-collection `environments/*.bru` now discovered (side effect of flatten)
- [x] Reorder persistence: `ChildOrder` metadata (`collection.json` + new `folder.json`) written on
      import and move, honored on load; `MoveNodeAsync` same-parent no-op removed
- [x] Rename keeps folder position in persisted order
- [x] Unit tests (Bruno import + linked reorder round-trips) — 7 new, green
- [x] `SwebKit.Core` + `SwebKit.App` build clean (0 errors)
- [ ] Manual in-app smoke pass (import the BOA workspace; drag-reorder; reload) — needs the user
      (MAUI Blazor Hybrid app can't be driven headlessly here)
- [ ] Aikido security scan (no Aikido tooling available in this session)

## Validation

- Automated: `SwebKit.Core.Tests` filtered run (Bruno + LinkedCollectionRoot) **37/37 passed**,
  including the 7 new tests. `SwebKit.Core` and `SwebKit.App` build with 0 errors.
- Backward compatibility: linked collections created before this change (no `ChildOrder`) load via
  the legacy folders-first/alphabetical fallback.

## Blockers

_None._

## Notes

- Existing wrapped import must be re-imported into a clean `.swebkit-api` to pick up the flattened
  layout (see index.md).
- Changes are in the working tree, **not committed** — the user commits themselves.
