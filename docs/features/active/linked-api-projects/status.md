# Status

**Status:** Review

## Tasks

- [x] Core: `LinkedRootId` on `ApiCollection`/`ApiEnvironment`; explicit `Id` persisted in
      collection/request/environment files; content-id file matching
- [x] Core: `SyncCollectionAsync` diff/write engine (stamp-guarded) + environment file sync
- [x] Core: capture-rule writes route to linked manifests (`ILinkedStoreWriter` on
      `PostRequestCaptureExecutor`)
- [x] Sidecar: `LinkedCollectionsService` (per-root load cache, merged views, lookups,
      Bruno write-back)
- [x] Sidecar: linked-roots endpoints (CRUD, reload, Bruno link import)
- [x] Sidecar: collections/environments PUT partition; execute-path merged resolution
- [x] Frontend: linked-root management dialog; tree badges + toolbar storage chip;
      import link mode; new-collection location picker; Git drawer linked-root picks;
      conflict banner with file list + overwrite/save-as-copy
- [x] Tests: xunit (sync engine, partition, conflicts) + vitest + Playwright
- [x] Docs updated

## Design notes captured during implementation

- **Stamp verification runs before the directory rename.** A collection rename moves the
  directory; checking known files by their load-time paths afterwards would match nothing and
  silently bypass conflict detection. The verify pass filters by request identity
  (`knownIds.Contains(file.RequestId)`) and runs before any `Directory.Move`.
- **Known-path fallback for id-mangled files.** An external edit can drop a request file's
  persisted `id` field, making content-id matching fail. Files owned at load are also matched by
  their recorded path, so `force` overwrite lands on the real file instead of producing a
  `name-1.swebreq.json` duplicate next to a foreign-looking leftover.
- **Every post-sync reload refreshes stamps.** `LinkedCollectionsService` calls
  `ReloadCoreAsync` after each successful sync/delete while holding the gate, so the next save
  verifies against post-save disk state — its own writes never self-conflict.
- **`ResolveApiRootPath` bare-folder mode.** A configured root without `.swebkit-api/` resolves
  to the folder itself; `AddLinkedRootAsync` calls `EnsureRootAsync` on add so every UI-linked
  root gets the `.swebkit-api` layout. Roots hand-created without the endpoint are tolerated.
- **Deletion scope.** `SaveCollectionsAsync` only deletes linked directories for roots that
  loaded cleanly (`IsValid`) — a missing folder must never delete anything.
- **Unknown `linkedRootId` markers are stripped.** A collection arriving with a root id the
  server doesn't know is saved to `collections.json` rather than dropped — the client marker is
  honoured only for roots that exist.

## Validation

| Check | Result |
|---|---|
| `dotnet test` Core | 1039 green (10 new `LinkedCollectionSyncTests`) |
| `dotnet test` Sidecar | 503 green (8 new `LinkedCollectionsPartitionTests`) |
| `tsc -b --force` | clean |
| `npm run test:unit` | 490 green (10 new `linked-roots.test.ts`) |
| Playwright `api-client-linked-roots` | 3/3 green — link via dialog, create-in-folder writes `.swebreq.json`, external-edit conflict → overwrite |
| Playwright api-client + layout + credentials + variables + deferred + contextual-assistant | 84/84 green (28 collection-creation call sites migrated to `createCollection` helper for the new dialog) |

Bugs found by the new tests during development:

- External edit that dropped the `id` field was treated as a foreign file → force-write produced
  a duplicate instead of overwriting (fixed via known-path matching).
- Collection rename ran before stamp verification → external edits silently overwritten
  (fixed via verify-before-rename).
- `BrunoSyncService` missing from DI → sidecar failed to start (caught by e2e global setup).
