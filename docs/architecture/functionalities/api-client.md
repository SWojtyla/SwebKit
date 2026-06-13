# API Client

## What Is Supported

- **Local collections and requests** — named collections with folder/request hierarchy, persisted through `CollectionRepository` to `collections.json` using atomic write and `.bak` recovery.
- **Git-linked API repositories** — user-selected repository folders containing `.swebkit-api/` roots. Linked collections and environments load beside local collections, and request saves write back to linked files with conflict detection.
- **Safe linked Git actions** — branch/status awareness, branch creation, branch dropdown switching, staged/unstaged API file review, in-app original/current diff preview, stage/unstage/revert for API-root files, staged commit, push, and provider-inferred remote compare links for GitHub/Azure DevOps.
- **Workflow trust UI** — target chip for local vs linked-repo creation context, commit preview for staged API files, and linked-save conflict actions: Reload from disk, Keep mine, Save as copy.
- **Variable substitution** — `{{token}}` syntax in URL, headers, body, GraphQL query, and GraphQL variables. Environment variables override collection variables on the same key.
- **Generated variables** — safe building blocks for integer, decimal, boolean, GUID, date/time, list, Bogus-backed fake data, and templates. Definitions are persisted; generated sample values are not.
- **Secrets** — Windows Credential Store and Azure Key Vault references. Secret values are resolved at send time and are not persisted to app-local JSON, linked files, or exports.
- **Environments** — named environment sets with plain, Windows Credential Store, Key Vault, and generated variables. Active environment is persisted in API client UI state.
- **REST execution** — all common HTTP methods, headers/query/body editing, auth injection, variable substitution, response status/timing/size/header/body display, and response history.
- **Authentication** — Bearer Token, API Key, Basic, OAuth 2 client credentials, and OAuth 2 authorization code with PKCE through MAUI `WebAuthenticator` using `sweb://oauth`.
- **Post-request capture** — JSONPath, response header, and status-code capture rules that write into collection or environment variables without scripting.
- **GraphQL** — query and variables editors, operation parsing, schema introspection cache, GraphQL error rendering, and `graphql-ws` subscriptions.
- **WebSocket** — URL/headers/subprotocol, connection state, bounded virtualized message log, text/binary composer, and saved message templates.
- **Export/import** — SwebKit-native JSON, Postman v2.1 subset import/export, Bruno export, standalone environment import, and full configuration bundle integration.
- **cURL portability** — copy selected REST/GraphQL requests as masked cURL commands and import cURL commands into the active collection.
- **Variable inspector** — list request tokens with source metadata and masked/resolved values.
- **Pinned requests** — session-local pinned request tabs with isolated dirty, response, and subscription message state.
- **Response examples** — save scrubbed response examples on requests; linked files persist examples only after explicit save.
- **Collection runner** — sequential folder/collection execution through the existing request execution path, with cancellation and per-request results.
- **Keyboard shortcuts** — API Client command registrations for new request, new collection, environment manager, send, and cancellation.

## Current Deferrals

- Pre-request scripts, arbitrary code execution, hosted collaboration, mock servers, gRPC, automatic cookie jar, and pull/rebase/stash remain out of scope.

## Core Runtime Flow

```text
ApiClientPage
  ├── CollectionRepository / EnvironmentRepository
  ├── LinkedCollectionRootRepository / LinkedCollectionFileService / LinkedGitService
  ├── CollectionTree
  │     ├── Local Collections
  │     └── Linked Repositories
  ├── RequestBuilderPanel
  │     ├── Params / Headers / Body / Auth / Capture
  │     ├── GraphQlPanel
  │     └── WebSocketPanel
  └── ResponseViewerPanel
        ├── response history
      ├── saved response examples
        ├── GraphQL errors / subscription messages
        └── body display cap + load-full affordance
```

## Send Path

1. `ApiClientPage` loads local collections/environments and linked roots.
2. User selects or creates a collection/request.
3. `RequestBuilderPanel` routes send based on request method: REST, GraphQL HTTP, GraphQL subscription, or WebSocket.
4. `HttpRequestExecutor` builds a resolved variable scope, applies auth, sends through the named `ApiClient` `HttpClient`, parses GraphQL errors, and runs capture rules.
5. Result flows back to `ApiClientPage`, updates request history, and renders through `ResponseViewerPanel`.

## Linked Root Save Path

1. Active request belongs to a linked collection.
2. `ApiClientPage.SaveActiveCollectionAsync` finds the linked root and expected request content stamp.
3. `LinkedCollectionFileService.SaveRequestAsync` compares the current content stamp with the expected stamp.
4. If unchanged, request metadata and sidecars are written atomically.
5. If changed externally, save returns a conflict and the UI offers Reload from disk, Keep mine, or Save as copy.

## Git Action Path

1. `LinkedGitService.GetStatusAsync` resolves the repository root and filters porcelain status to the linked API root.
2. UI shows branch, changed API file count, staged/unstaged sections, changed file details, and commit preview for staged files.
3. Review loads original/current text for a changed API file inside SwebKit.
4. Stage/unstage/revert operations validate the file is one of the reported linked API files before invoking Git.
5. Staged commit rejects unrelated staged files outside the API root.
6. Push and remote compare use the detected repository remote/branch.

## State Persistence

| State                               | Location                                   | Lifetime                                  |
| ----------------------------------- | ------------------------------------------ | ----------------------------------------- |
| Local collections and requests      | `AppData/collections.json`                 | Persistent                                |
| Local environments and API UI state | `AppData/environments.json`                | Persistent                                |
| Linked root registrations           | `AppData/api-linked-roots.json`            | Persistent, machine-local                 |
| Linked collections and requests     | `.swebkit-api/collections/**`              | Persistent, Git-trackable                 |
| Linked environments                 | `.swebkit-api/environments/*.swebenv.json` | Persistent, Git-trackable references only |
| Secret values                       | Windows Credential Store or Key Vault      | Persistent outside repo files             |
| Request history                     | `ApiClientPage._requestHistory`            | Session only                              |
| Pinned requests                     | `ApiClientPage._pinnedRequestIds`          | Session only                              |
| Response examples                   | `HttpRequestEntry.ResponseExamples`        | Persistent with collection/request        |
| WebSocket message log               | `WebSocketPanel` state                     | Session/request only                      |
| GraphQL subscription messages       | `ApiClientPage._subscriptionMessages`      | Session/request only                      |
| OAuth2 token cache                  | `OAuth2TokenManager` memory cache          | Session only                              |
| Generated sample values             | request scope/preview only                 | Not persisted                             |

## Security and Safety Notes

- Linked root files store secret references only.
- Export formats never include secret values.
- Generated variables are non-secret and cannot execute code.
- Git operations are fixed command builders, not arbitrary command execution.
- Git status, staging, revert, and commit actions are scoped to linked API root files.
- Key Vault failure degrades gracefully rather than crashing request execution.

## Validation Focus

- repository atomic write and backup recovery
- linked-root sparse request read/write and sidecar handling
- linked environment and generated variable serialization
- secret-reference-only persistence
- Git path scoping for status, stage, unstage, revert, and staged commit
- request execution with auth, variable substitution, generated values, and capture rules
- UI state for tree selection, linked-root targeting, environments, generated-variable editors, and response rendering
