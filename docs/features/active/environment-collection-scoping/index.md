# Environment Collection Scoping

## Context

Follow-on to [bruno-import-and-reorder](../bruno-import-and-reorder/index.md). SwebKit stored API-client
environments as a single **global** pool: `ApiEnvironment` had no scope, linked repos kept all envs in
a root-level `.swebkit-api/environments/`, and the active environment was one global scalar. Bruno,
by contrast, scopes environments **per collection** (plus optional global ones). Importing the BOA
workspace (3 collections, each with `DEV`/`STG`/`PRD` and `DEV (via APIM)` / `DEV (via POD)` variants)
therefore dumped everything into one flat picker with duplicate-looking names.

**Goal:** give environments an optional collection scope (null = global), filter the picker to the
active collection's environments + global ones, remember the active environment per collection, and
scope Bruno-imported environments to their originating collection. Existing environments stay global
(non-destructive).

## Decisions (user-confirmed)

- Picker shows the active collection's environments **plus** global (unscoped) ones.
- New environments default to the **active collection**, with a selector for **Global** / another collection.
- Active environment is remembered **per collection**, with the global `ActiveEnvironmentId` as fallback.

## Changes

### Core
- `Domain/ApiClientModels.cs` — `ApiEnvironment.CollectionId` (null = global; persisted locally, derived
  from disk location for linked repos); `ApiClientUiState.ActiveEnvironmentIdByCollection` (mirrors the
  existing `LastSelectedRequestIdByCollection`), with `ActiveEnvironmentId` kept as the global fallback.
- `Configuration/EnvironmentRepository.cs` — `AddEnvironmentAsync(name, collectionId?)`,
  `SetActiveEnvironmentForCollectionAsync`, and delete now clears per-collection active refs too.
- `Services/BrunoFolderImporter.cs` — imported `environments/*.bru` are scoped to their collection.
- `Services/CollectionImportService.cs` — remaps the importer's temporary collection id → persisted id
  for env scope; env-name dedupe is **per scope** (two collections may each keep a `DEV`); linked import
  routes scoped envs under the collection folder and global ones to the root (`WriteImportToLinkedRootAsync`).
- `Services/LinkedCollectionFileService.cs` — global env → `.swebkit-api/environments/`, collection env →
  `collections/<coll>/environments/`; `ReadNodesAsync` skips the `environments/` folder (so it isn't a
  tree node); `ReadEnvironmentsFromFolderAsync` sets `CollectionId` from location;
  `WriteEnvironmentToCollectionAsync` added; `WriteCollectionToLinkedRootAsync` returns the collection dir.

### App
- `ApiClientPage.razor` — `ActiveEnvironment` resolves per active collection (per-collection selection →
  global fallback), ignoring a resolved env scoped to a different collection.
- `ApiClientPage.Collections.cs` — `SelectEnvAsync` stores the choice per active collection.
- `ApiClientToolbar.razor` (+`.css`) — picker filtered to global + active-collection envs, grouped under
  "Global" / the collection name; active marker uses the resolved `ActiveEnvironment`.
- `EnvironmentEditor.razor` (+`.css`) — scope `<select>` (Global + local collections); `EnvironmentManagerPanel.razor`
  passes local collections + active-collection id and defaults new envs to the active collection; the
  edit-clone now carries `CollectionId` (fixed latent bug where editing dropped scope).

## Status

See [status.md](status.md). Current: `Review`.

## Out of scope / deferred
- **Linked env scope reassignment** (moving a `.swebenv.json` between root and a collection folder): there
  is no UI to edit a linked environment's scope today (the manager panel edits **local** envs only; the
  only linked-env write path is in-place secret configuration). Building `MoveEnvironmentScopeAsync` now
  would be dead code — deferred until linked-env editing exists. Local scope editing works via the editor.
- Parsing the workspace-level `bruno-global-environments.json` (would map to global envs).
