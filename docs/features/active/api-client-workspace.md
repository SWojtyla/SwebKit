# API Client workspace model — file visibility + linked roots

State: In Progress

## Goal

Make the API Client a Bruno-grade daily driver. Two slices:

- **A — Clarity pass.** The store is hidden, imports are invisible and unexplained,
  cURL import exists but nobody can find it, and the Git panel watches a repo the
  app never writes to. Surface where data lives, make every import flow
  previewable and explicit, and make the Git boundary honest.
- **B — Linked roots.** Activate the dormant Core subsystem
  (`LinkedCollectionFileService`, `LinkedGitService`,
  `LinkedCollectionRootRepository` — all implemented and unit-tested, never wired
  to the sidecar or React). A linked root is a folder on disk holding a
  `.swebkit-api/` tree (`swebkit.json` manifest, collections as directories,
  `.swebreq.json` per request, `.swebenv.json` environments, `folder.json`
  ordering) — optionally inside a git repo. Collections load from and save to
  those files directly, giving the Bruno model: your data is files you own, and
  git is just git on that folder.

## Current state (verified)

- All collections live in one `collections.json` under `%AppData%`; environments
  in `environments.json`. Location shown only in Settings → API Client
  (truncated path + Copy). No reveal-in-explorer Tauri command exists.
- The Git panel stages/commits in a user-picked repo — but no app write ever
  lands there. It is functionally disconnected from the collections store.
- cURL import exists (`POST /api/api-client/import-curl`,
  `CurlImportDialog`): single command only, no `-F`, no `^` continuations,
  ignored flags are silently dropped, hidden behind an unlabeled icon.
- Collection import exists (`POST /api/config/collections/import`): SwebKit
  JSON + Postman v2.1 file, or Bruno folder — both one-way copies into
  `collections.json`, no preview, no destination choice, collisions get
  `" (2)"` suffixes with no warning surfaced.
- `LinkedCollectionRootRepository.LoadAsync()` is deliberately never called in
  `src-sidecar/Program.cs` ("linked collections aren't a sidecar feature yet"),
  so `ApiClientAgentService` sees local collections only.
- `CollectionImportService` already has
  `ImportCollectionToLinkedRootAsync`/`ImportBrunoFolderToLinkedRootAsync` —
  unreachable.
- `LinkedGitService` is a complete API-scoped git client: status, branches,
  diff, stage/unstage/revert per file, commit, push, remote compare URL.

## Slice A scope

- **cURL parser** (`ApiClientWorkflowService.ImportCurl` + endpoint):
    - Multi-command paste (several `curl` invocations separated by newlines /
      `&&` / `;`) → multiple requests.
    - `^` line continuations (cmd.exe paste) normalized like `\`.
    - `-F/--form` → `RequestBodyMode.FormData` with `FormDataField` entries
      (`name=value`, `name=@path` → `IsFile`).
    - `-b/--cookie`, `-A/--user-agent`, `-e/--referer` → headers.
    - `-k/--insecure` → warning (app-level SSL verify still applies).
    - Common ignorable flags consume their values silently; unknown `-` flags →
      `warnings[]` entry instead of silent drop.
    - **Breaking response shape**: `{ requests: HttpRequestEntry[], warnings:
string[] }` (was `HttpRequestEntry`). `importCurlRequest` in
      `web/src/lib/api/apiClient.ts` and all callers/tests updated.
- **Reveal in explorer**: Rust `reveal_in_explorer(path)` (Windows
  `explorer /select`, macOS `open -R`, Linux `xdg-open` parent) +
  `revealInExplorer()` in `tauri-bridge.ts`.
- **Import dialog**: destination picker ("Internal store" vs — after Slice B —
  a linked root), post-import summary names where data landed
  (`collections.json` vs repo path), collision warnings surfaced.
- **cURL dialog**: multi-request preview list, warnings panel, "import" and
  "open as tab without saving" actions, destination picker kept.
- **CollectionTree**: workspace footer shows the store path (reveal + copy
  buttons); labeled import buttons instead of bare icons.
- **GitPanel**: honesty line — "This repository is not a linked collection
  root; nothing the app saves lands here." when the repo holds no
  `.swebkit-api` (pre-B state), removed when B lands.
- **Settings → API Client**: Reveal button next to Copy path.

## Slice B scope

### Contracts

`GET /api/config/collections/store` gains a `linkedRoots` array alongside
`collections`:

```jsonc
{
  "collections": [ /* internal store, unchanged */ ],
  "concurrencyToken": "...",
  "linkedRoots": [{
    "id", "name", "path", "apiRootPath",
    "isGitRepository", "repositoryRoot", "branch", "changedFileCount",
    "isValid", "diagnostics": [],
    "collections": [ ApiCollection ],
    "environments": [ ApiEnvironment ],
    "requestFiles": [{ "requestId", "requestFilePath", "contentStamp" }],
    "environmentFiles": [{ "environmentId", "environmentFilePath" }]
  }]
}
```

New endpoints `src-sidecar/Endpoints/LinkedRootsEndpoints.cs`:

```
GET    /api/linked-roots
POST   /api/linked-roots                      { path, name?, brunoSyncFolderPath? }
PATCH  /api/linked-roots/{rootId}             { name?, isEnabled?, brunoSyncFolderPath?, brunoSyncEnabled? }
DELETE /api/linked-roots/{rootId}             unregister (never deletes files)
POST   /api/linked-roots/{rootId}/reload
POST   /api/linked-roots/{rootId}/collections                 { name }
PATCH  /api/linked-roots/{rootId}/collections/{collectionId}  { name }
DELETE /api/linked-roots/{rootId}/collections/{collectionId}
POST   /api/linked-roots/{rootId}/collections/{collectionId}/requests  { name, parentFolderId?, request? }
PUT    /api/linked-roots/{rootId}/collections/{collectionId}/requests/{requestId}  { request, contentStamp? }
DELETE /api/linked-roots/{rootId}/collections/{collectionId}/requests/{requestId}
POST   /api/linked-roots/{rootId}/collections/{collectionId}/folders   { name, parentFolderId? }
PATCH  /api/linked-roots/{rootId}/collections/{collectionId}/folders/{folderId}  { name }
DELETE /api/linked-roots/{rootId}/collections/{collectionId}/folders/{folderId}
PATCH  /api/linked-roots/{rootId}/collections/{collectionId}/nodes/{nodeId}/move { parentFolderId? }
PUT    /api/linked-roots/{rootId}/collections/{collectionId}/order     { parentFolderId?, orderedChildIds[] }
POST   /api/linked-roots/{rootId}/environments        { environment }
PUT    /api/linked-roots/{rootId}/environments/{envId} { environment }
DELETE /api/linked-roots/{rootId}/environments/{envId}
```

- `PUT .../requests/{id}` returns `409 { currentContentStamp, requestFilePath }`
  on stamp mismatch (same conflict semantics as the existing collections PUT).
- `POST /api/config/collections/import` gains `linkedRootId?` → routes to
  `ImportCollectionToLinkedRootAsync` / `ImportBrunoFolderToLinkedRootAsync`.
- `Program.cs` calls `LinkedCollectionRootRepository.LoadAsync()` at startup —
  agent tools then see linked collections too (free win).
- All linked-root endpoints reject with the standard demo-mode error in demo
  mode (they touch real disk).

### Frontend

- `ApiCollection.origin?: { kind: "internal" | "linked" | "demo"; rootId?;
rootName?; rootPath? }` — assigned by the frontend when flattening
  `linkedRoots`, never persisted.
- `ApiEnvironment` gains the same optional `origin` + `filePath`.
- CollectionTree shows "Internal store" section header (path, reveal) plus one
  section per linked root: name, path, git badge (branch + changed count when
  `isGitRepository`), reveal, reload, remove, git drawer shortcut.
- `ApiClientPageContext` routes mutations by origin: linked collections call
  the linked-roots endpoints; internal stays on the serialized whole-store PUT.
- Request save in linked collections goes through
  `PUT .../requests/{id}` with `contentStamp`; `409` surfaces
  reload/overwrite actions reusing the existing conflict-banner pattern.
- Environments: linked envs merge into the picker/manager (tagged); edits route
  to `PUT /linked-roots/{rootId}/environments/{envId}`.
- Import dialog destination = Internal store | linked root.
- Git drawer: linked roots inside a git repo appear as preconfigured repos —
  `repositoryRoot` + `apiSubpath` = relative path to `.swebkit-api`. Root
  section's Git button opens the drawer with that repo selected.
- Linked collections are read/write peers — drag/drop, reorder, rename, delete
  all work through the endpoints, not the whole-store PUT.

## Non-goals

- **Bruno write-back** (`BrunoSyncFolderPath`/`BrunoSyncEnabled` mirror writes
  to `.bru` files): the config surface exists; the file-mirror implementation
  is follow-up. Importing Bruno into a linked root already keeps data on disk.
- Two-way sync/watch of linked folders (fs-watcher, external edit detection
  beyond the content-stamp conflict on save). `Reload` is the manual answer.
- Moving collections between internal and linked storage (export→import is
  the workaround).
- Git operations beyond what `LinkedGitService` already implements.
- OpenAPI/Swagger import — separate feature.
- Demo mode gets no linked roots (real-disk feature); demo collection stays
  internal.

## Implementation tasks

### Slice A

- [ ] cURL parser: multi-command, `^` continuations, `-F`, `-b/-A/-e` headers, warnings
- [ ] `import-curl` endpoint → `{ requests[], warnings[] }` + tests
- [ ] `reveal_in_explorer` Tauri command + `revealInExplorer` bridge
- [ ] Import dialog: destination + where-it-landed summary + collision warnings
- [ ] cURL dialog: multi preview, warnings, open-as-tab
- [ ] CollectionTree footer: store path + reveal + labeled import buttons
- [ ] GitPanel honesty banner; Settings reveal button
- [ ] e2e updates

### Slice B

- [x] `LinkedCollectionRootRepository.LoadAsync()` at sidecar startup
- [x] `linkedRoots` in `/api/config/collections/store`
- [x] `LinkedRootsEndpoints.cs` + import `linkedRootId` routing + tests
- [ ] Frontend: types/origin, tree sections, mutation routing, save conflict,
      env routing, import destination, git-drawer integration
- [ ] e2e: link a temp root, CRUD round-trip, conflict path, import-to-root

## Test plan

- Core: cURL parser cases (multi, `^`, `-F` variants, cookies, `-u`,
  unknown flags → warnings, name-from-url).
- Sidecar: linked-roots CRUD + every mutation endpoint happy/error path
  (missing root, missing collection, stamp conflict → 409, demo-mode reject),
  store merge shape, import target routing.
- Vitest: origin routing helpers, tree section rendering, warnings rendering.
- Playwright api-client spec: import preview flow, linked root e2e (create
  `.swebkit-api` temp dir via the API, link it, create/save/rename/delete a
  request, verify the `.swebreq.json` file on disk), reveal-button presence.
- Aikido scan on all touched first-party code.

## Decisions

- Linked collections ride in the same `/collections/store` response as a
  separate `linkedRoots` array rather than a merged `collections` array — the
  internal PUT contract stays untouched, and origin is unambiguous at
  flatten time.
- Per-node endpoints for linked mutations rather than a whole-root PUT:
  matches the file-per-request model and `LinkedCollectionFileService`'s
  method granularity; avoids turning the linked world back into a blob.
- The GitPanel stays a generic repo panel; linked roots appear inside it as
  configured repos (root + apiSubpath) rather than a second git UI.
- Import into a linked root writes `.swebreq.json` (not `.bru`) — the linked
  root is SwebKit's format; Bruno write-back is a separate sync feature.

