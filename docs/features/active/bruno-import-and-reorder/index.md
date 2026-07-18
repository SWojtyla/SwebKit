# Bruno Multi-Collection Import & Persisted Tree Reorder

## Summary

Three related fixes to the API Client, driven by importing a large real-world Bruno workspace
(`C:\Projects\BOA\boa-testresources-src\Bruno`, ~473 requests across 3 collections) into a linked
git repo:

1. **Flatten multi-collection imports.** Pointing the importer at a *workspace* folder (a folder
   that is not itself a Bruno collection but groups several that are) used to produce a single
   wrapper collection named after that folder, with each real collection buried one level down.
   Now each child collection is imported as its own top-level collection — no wrapper.
2. **Import per-collection environments/variables.** These were silently dropped for workspace
   imports. Same root cause as (1): the importer only scanned `<picked>/environments`, which does
   not exist at the workspace level. Fixing (1) makes each collection's own `environments/*.bru`
   discoverable, so environments now import.
3. **Persist drag-and-drop reorder for linked collections.** Reorder already worked for local
   collections and for cross-parent moves; same-parent reordering within a *linked* (git-repo)
   collection was a deliberate no-op because linked trees were always read back alphabetically.
   Order is now persisted as `ChildOrder` metadata and honored on load.

## Status

See [status.md](status.md). Current: `Review`.

## Root cause (shared by 1 & 2)

`C:\Projects\BOA\...\Bruno` has no `bruno.json`; its children (`Brio Open API`, `GraphQL`,
`Middletier`) each do. `BrunoFolderImporter` treated the picked folder as one collection, folded the
children in as subfolders, and only looked for `environments/` at the (wrong) top level — and
actively skips nested `environments/` directories during the walk. So the wrapper appeared and no
environments were found.

## Changes

### Core

- `src/SwebKit.Core/Services/BrunoFolderImporter.cs`
  - `ImportFromFolderAsync` now detects a multi-collection workspace (no `bruno.json` at the picked
    folder, but ≥1 immediate subdirectory has one) and imports each child as a separate collection.
  - Single-collection body extracted to `ImportSingleCollection` (unchanged behavior for the
    single-collection and no-manifest fallback paths).
- `src/SwebKit.Core/Domain/LinkedCollectionModels.cs`
  - `SwebKitCollectionManifest.ChildOrder` (new) and `SwebKitFolderManifest` (new `folder.json`
    sidecar) — ordered lists of on-disk base names (folder dir names + request file names).
- `src/SwebKit.Core/Services/LinkedCollectionFileService.cs`
  - Import (`WriteCollectionToLinkedRootAsync` / `WriteNodesToDirectoryAsync`) writes `ChildOrder`
    into `collection.json` and a `folder.json` per folder, preserving Bruno's `seq` order.
  - Read (`ReadNodesAsync` + new `OrderNodes`) sorts siblings by `ChildOrder`, falling back to the
    legacy folders-first/alphabetical layout when no order is persisted (backward compatible).
  - `MoveNodeAsync` no longer no-ops same-parent reorders; it persists the destination parent's
    order from the in-memory (post-move) tree via new `PersistChildOrderAsync`.
  - `RenameFolderAsync` updates the parent's `ChildOrder` entry so a renamed folder keeps its slot.

### Tests

- `tests/SwebKit.Core.Tests/BrunoFolderImportTests.cs` (new) — workspace flattening, per-collection
  environment import, single-collection path.
- `tests/SwebKit.Core.Tests/LinkedCollectionRootTests.cs` — import order round-trip, same-parent
  reorder persistence, cross-parent reorder destination order, rename keeps position.

## Notes / follow-ups

- **Re-import needed for the existing wrapper.** The user's first import already wrote the wrapped
  structure into the linked `.swebkit-api`. To get the flattened layout, delete the previously
  imported collections under `.swebkit-api/collections` and re-import the workspace folder (a fresh
  import into a clean root avoids name-suffixed duplicates).
- **Secret values are not imported** (by existing design): Bruno `vars:secret` and bearer tokens
  come in as empty placeholders requiring re-entry.
- **Cross-collection drag** (moving a request from one top-level collection into another) remains
  unsupported — reorder is within a single collection. Out of scope here.
- The workspace-level `bruno-global-environments.json` (a different, exported-JSON format) is not
  parsed; only per-collection `environments/*.bru` are. Candidate follow-up.
