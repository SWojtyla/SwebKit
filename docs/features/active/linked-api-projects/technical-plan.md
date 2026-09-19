# Technical Plan — Linked API Projects

## Architecture

The React app keeps its whole-store mental model; the sidecar owns the partition.

```
React store PUT ──► ConfigEndpoints.SaveCollectionsAsync
                        │
                        ├─ linked collections ─► LinkedCollectionsService.SyncCollectionAsync
                        │                          └─ LinkedCollectionFileService.SyncCollectionAsync
                        │                             (diff vs last-loaded state, stamp-guarded)
                        └─ local collections ────► CollectionRepository → collections.json
```

### Layers

- **Core (`LinkedCollectionFileService.Sync.cs`)** — whole-collection reconcile:
  plan desired layout → verify content stamps → rename dir → write/move request files →
  write manifests → delete absent known requests → prune empty dirs. Environment sync via
  `SyncEnvironmentAsync` (move-on-rename) + `DeleteEnvironmentFileAsync`.
- **Sidecar (`LinkedCollectionsService`)** — owns cached `LinkedCollectionRootLoadResult`s,
  a `_gate` serializing all mutations, reload-after-write to keep stamps fresh, Bruno
  write-back on save/delete, and `ILinkedStoreWriter` for capture-rule writes.
- **Endpoints** — `LinkedRootsEndpoints` (CRUD/reload) + partition logic inside
  `ConfigEndpoints` PUTs + merged GETs + merged collection/env resolution on `/execute`.
- **Frontend** — `useLinkedRoots` + mutations, `LinkedProjectsDialog`, `NewCollectionDialog`
  storage picker, tree storage badge, toolbar storage chip, env "Store in" select,
  `ConflictError.conflicts` → banner with file list + Reload/Overwrite/Save-as-copy,
  Git panel one-click linked-root picks scoped to `.swebkit-api` via `apiSubpathFor`.

## Identity and conflict rules

- `collection.json` persists `Id`/`Name`; `.swebreq.json` persists `id`; `.swebenv.json`
  persists `id`. Path-derived `StableId` is only a fallback for files written before this
  feature.
- Stamp check = per-request-file hash (file + sidecars) captured at load. A mismatch on any
  file owned at load → `409 { error, conflicts: string[] }` — nothing is written unless
  `force=true`.
- Files never seen at load are foreign: never deleted, never overwritten (name collisions get
  a `-N` suffix).
- Deletion only runs for roots that loaded cleanly (`IsValid`).

## API surface

- `GET /api/api-client/linked-roots` → `LinkedRootSummary[]` (validity, diagnostics, git status)
- `POST /api/api-client/linked-roots` `{ path, name?, brunoFolderPath? }` — creates
  `.swebkit-api/`, registers the root, optionally imports the Bruno folder + enables write-back
- `PUT /api/api-client/linked-roots/{id}` `{ name?, isEnabled?, brunoSyncFolderPath?, brunoSyncEnabled? }`
- `DELETE /api/api-client/linked-roots/{id}` — unlink only; files stay on disk
- `POST /api/api-client/linked-roots/reload`
- `PUT /api/config/collections?concurrencyToken=&force=` — partitioned store save
- `PUT /api/config/environments` — partitioned environment save

## Capture rules

`PostRequestCaptureExecutor` takes an optional `ILinkedStoreWriter`. When the target
collection/environment is linked, captures write the linked manifest/`.swebenv.json` through it
instead of the local repositories.
