# Test Plan — Linked API Projects

## xUnit — sync engine (`LinkedCollectionSyncTests`)

- New collection → directory + manifest + request files written with persisted ids
- Request rename → file renamed, persisted id kept
- Request delete → file removed; foreign files preserved
- Request moved into folder → file moved, folder manifest written
- Collection rename → directory renamed, manifest id stable
- External edit → conflict, nothing written (including when combined with a collection rename)
- `force` → overwrite wins
- Environment sync: write, rename-moves-file, id persisted

## xUnit — sidecar partition (`LinkedCollectionsPartitionTests`)

- Linked collection in store PUT → files written, `collections.json` untouched for it
- New collection with `linkedRootId` → lands in the linked folder
- Unknown `linkedRootId` → falls back to app storage (marker stripped, never dropped)
- Deleted linked collection → directory removed
- External edit → 409 + conflicts, neither file nor `collections.json` written
- `force` → overwrite
- `concurrencyToken` mismatch → 409
- GET store → merged local + linked with `linkedRootId` markers

## Vitest (`linked-roots.test.ts`)

- `apiSubpathFor`: separators, casing, nesting, repo-root equality, outside-repo and
  sibling-prefix traps → `null`
- `apiSend` 409 → `ConflictError` carrying `conflicts`; non-JSON/409 and non-409 bodies

## Playwright (`api-client-linked-roots.spec.ts`)

- Link a folder via the dialog's manual path input → `.swebkit-api/` created, root listed
- New collection "Store in" → linked root → request saves as `.swebreq.json` on disk;
  storage badge + toolbar chip show the root; nothing leaks into `collections.json`
- External file edit → conflict banner names the file → Overwrite writes the draft

## Regression

- All `api-client*` and `contextual-assistant` specs (28 collection-creation sites migrated to
  the `createCollection` helper since New Collection is its own dialog now)
- `dotnet test` Core/Sidecar/Agents/Azure suites; MAUI build (shared Core models)
