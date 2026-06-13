# Frontend — API Client

## Current State

The API Client UI is implemented as a routed MAUI Blazor page under `src/SwebKit.App/Components/ApiClient/ApiClientPage.razor`. Supporting controls live in the same `Components/ApiClient/` folder, with matching `.razor.css` files for isolated styling.

The page uses a single-request focus model: one request is active in the request builder, with response/history state shown alongside it. Post-Phase-10 follow-up work may add pinned request tabs, but that should not happen until per-request dirty, response, subscription, and WebSocket lifecycle state is explicitly isolated.

## Layout

- Top toolbar: new collection, new request, target chip, linked repo actions, cURL import/copy, variable inspector, pin/unpin, collection runner, save, conflict actions, collection variables, export/import, environment manager, active environment picker.
- Left pane: `CollectionTree.razor` with flattened/virtualized tree rows.
- Right pane: request builder and response viewer split by JS-resizable panes.
- Management screens: environments, collection variables, API repositories, Git panel.
- Dialogs: new collection, add API repository, configure linked secret, delete confirmation, export/import.

## Component Map

| Component                                                   | Responsibility                                                                                                                            |
| ----------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| `ApiClientPage.razor`                                       | Page orchestration, repositories, linked roots, active request, save flow, Git actions, environment picker, keyboard command subscription |
| `CollectionTree.razor`                                      | Local and linked collection tree, search/filter, expand/collapse, request/collection/linked-root selection, rename/delete context menu    |
| `RequestBuilderPanel.razor`                                 | Method/URL editor, Params/Headers/Body/Auth/Capture tabs, send routing for REST, GraphQL, and WebSocket/subscription modes                |
| `ResponseViewerPanel.razor`                                 | Status, headers/body/raw views, GraphQL errors, response history, subscription messages, 500 KB display cap                               |
| `EnvironmentManagerPanel.razor` / `EnvironmentEditor.razor` | Environment CRUD, plain/credential/KV/generated variable editing                                                                          |
| `CollectionVariableEditor.razor`                            | Collection variable editing, including generated variables                                                                                |
| `VariableGeneratorEditor.razor`                             | Building-block generator editor for integer, decimal, boolean, GUID, date/time, list, faker, and template values                          |
| `AuthPanel.razor` and auth forms                            | None/inherited/Bearer/API key/Basic/OAuth 2 auth editing                                                                                  |
| `GraphQlPanel.razor`                                        | GraphQL query/variables editors, operation parsing, schema introspection                                                                  |
| `WebSocketPanel.razor`                                      | Connect/disconnect, message log, text/binary composer, saved message templates                                                            |
| `CollectionExportDialog.razor`                              | SwebKit/Postman/Bruno export and collection/environment import                                                                            |

## Collection Tree and Linked Roots

The tree has two durable groups:

- Local Collections
- Linked Repositories

Linked repository rows show branch and clean/dirty status when Git metadata is available. Selecting a linked root makes it the target for new collection creation; selecting a collection/request clears or updates the active target as appropriate.

Linked-root management is surfaced through the API Repos screen and Git panel. The Git panel currently supports:

- branch summary and changed API file count
- branch create
- branch switch via dropdown of available local branches
- visible target chip for local vs linked-repo creation context
- staged and unstaged changed-file sections
- in-app original/current diff preview for changed API files
- commit preview with branch, remote, and staged API files
- stage, unstage, and revert API files under the linked root
- linked-save conflict actions: Reload from disk, Keep mine, Save as copy
- commit staged API files
- push current branch
- open remote compare for GitHub/Azure DevOps remotes when inferable

## Request Builder

Supported request modes:

- REST: GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS
- GraphQL: query/mutation over HTTP, plus `graphql-ws` subscriptions
- WebSocket: text/binary send and receive

The builder uses shared key/value grids for headers, query params, and form data. Auth is inheritable from folder or collection. Capture rules are edited as no-code rows and run after request execution.

## Request Portability and Variable Inspector

The page supports Copy as cURL and Import from cURL through `ApiClientWorkflowService`.

- Copy as cURL resolves plain/generated values and masks secret-backed values by default.
- Import from cURL maps method, URL, headers, and raw body into a new request in the active collection.
- The variable inspector lists every `{{token}}` used by the selected request, showing source and masked/resolved value.
- Unresolved variables remain visible without blocking editing.

## Pinned Requests and Response Examples

Pinned request tabs sit above the workspace and preserve per-request dirty state, last response, and GraphQL subscription messages. They are session-local UI state for now.

Response examples can be saved from `ResponseViewerPanel`. Examples are stored on `HttpRequestEntry.ResponseExamples`, with secret-looking headers and JSON properties masked before persistence.

## Variables and Secrets UI

Variables can live at collection or environment scope. Environment variables override collection variables with the same key. Supported variable types are:

- plain value
- Windows Credential Store reference
- Azure Key Vault reference
- generated value

Generated values use safe building blocks, not scripts. The UI stores generator definitions only; generated sample values are not persisted.

Secret-backed values are masked in preview surfaces. Missing linked secrets show a configure-secret prompt that stores local values in `ICredentialStore` and writes only references back to linked files.

## Export and Import UI

The export/import dialog supports:

- SwebKit-native collection JSON
- Postman Collection v2.1 import/export subset
- Bruno export as zipped `.bru` files
- standalone environment import
- configuration bundle integration through the existing settings bundle flow

Postman/Bruno remain projections; SwebKit's app-local model and linked-root folder format are the source schemas.

## Collection Runner

The Run toolbar action opens the collection runner screen and executes the active collection sequentially through `ApiClientCollectionRunnerService`. The runner reuses the same request execution path as single sends, reports per-request status and elapsed time, supports cancellation, and skips WebSocket requests with an explicit result row.

## Keyboard Shortcuts

| Shortcut       | Action                                   |
| -------------- | ---------------------------------------- |
| `Ctrl+N`       | New request in active collection         |
| `Ctrl+Shift+N` | New collection                           |
| `Ctrl+E`       | Toggle environment manager               |
| `Ctrl+Enter`   | Send current request                     |
| `Escape`       | Cancel in-flight request where supported |

## Performance Guards

- `CollectionTree.razor` uses a flattened list with `<Virtualize>`.
- WebSocket message log is virtualized and capped.
- GraphQL subscription messages are capped at 1,000 in page state.
- Response body display is capped at 500 KB until the user loads the full response.
- Monaco assets are pre-warmed when the API Client page initializes and editor interop waits for DOM availability.

## Follow-Up Notes

The post-Phase-10 roadmap is implemented. Future API Client UI work should be treated as a new feature slice rather than extending this roadmap implicitly.
