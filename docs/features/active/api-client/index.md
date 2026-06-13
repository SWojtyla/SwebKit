# API Client

## Goal

Add a Postman/Insomnia/Bruno-alike API client to SwebKit that supports REST, GraphQL, and WebSocket
requests with rich editing, environments/secrets, authentication, and multi-format collection
import/export — without leaving the SwebKit desktop session.

## Why

SwebKit users already work with Azure services (Service Bus, AKS, Redis, DevOps, App Insights) from
one desktop tool. Adding an API client eliminates the context switch to a separate tool and enables
future integration (e.g., testing an Azure API endpoint while viewing its App Insights trace in the
same session). Key Vault secret resolution also gives it an edge over generic tools for Azure-heavy
workflows.

## Scope

- **Request types:** REST (all HTTP methods), GraphQL (query + variables), WebSocket (connect/send/listen)
- **Syntax highlighting and autocomplete:** Monaco Editor for all editors; GraphQL schema
  introspection with schema-aware autocomplete
- **Collections:** named top-level collections with nested folder/request hierarchy
- **Git-linked SwebKit roots:** user-configured local repository folders containing SwebKit-owned
  API collection files; multiple roots can be opened at the same time in the collection tree
- **Environments and variables:** `{{variable}}` substitution in URL, headers, body; multiple named
  environments; active-environment switcher; **two-level scope hierarchy** — collection-level
  variables (always active, no environment required) override-able by environment-level variables
  when an environment is selected
- **Post-request capture rules:** JSONPath-based building blocks that extract values from a
  response (body, header, status code) and store them in a collection or environment variable
  automatically — no scripting, no code writing
- **Inline variable preview:** `{{variable}}` tokens in the URL bar and body editor show their
  resolved value as an inline badge so you can verify substitution before sending
- **Secrets:** environment variables backed by plain values, Windows Credential Store references, or
  Azure Key Vault secret references (KV uses `DefaultAzureCredential`; setup required in Settings)
- **Authentication:** Bearer token, API Key (header or query param), Basic, OAuth 2 (client
  credentials + authorization code via MAUI `WebAuthenticator`); auth is **inheritable** from
  parent folder or collection — a request with no auth set uses the nearest ancestor's auth config
- **Persistence:** `collections.json` + `environments.json` in `%APPDATA%/SwebKit/` — same
  atomic-write + `.bak` recovery pattern as all other repositories
- **Export/Import:**
  - SwebKit-native versioned JSON (`SwebKitCollectionV1`) — primary internal format
  - Postman Collection v2.1 — export + import (subset; test scripts excluded); Postman collection
    variables extracted as a new SwebKit environment on import
  - Bruno `.bru` — export as zip of folder-per-request files (import Phase 7 follow-up)
  - Environments importable standalone (SwebKit format + Postman variable extraction)
- **Git-friendly collection format:** a SwebKit-native folder format with one request per file,
  optional body/query sidecar files, root-level schema versioning, and secret references only
- **Full-bundle integration:** `collections.json` + `environments.json` included in the existing
  `ConfigurationBundleService` bundle export/import
- **Standalone collection export:** per-collection export independent of the full bundle
- **Auto-save:** opt-in user setting (default: off); debounced 500 ms after last edit
- **Performance:** `Virtualize` component on collection tree and message log from Phase 1; response
  body cap at 500 KB (load-more on demand)

## Non-Goals (Phase 1–8 scope guards)

- No automated test runner / collection runner
- No pre-request scripts. Post-request capture rules are limited to JSONPath-based variable
  extraction building blocks — no arbitrary code execution
- No mock server functionality
- No team collaboration or cloud sync
- No hosted collaboration or cloud sync beyond user-managed Git repositories
- No gRPC support
- No response assertions or test assertions
- No automatic cookie jar management
- No Bruno import (export-only in Phase 7; import deferred)
- No drag-and-drop reordering (deferred to post-Phase-8 follow-up)
- No cookie jar management

## Phases

| Phase | Name                     | Summary                                                                            |
| ----- | ------------------------ | ---------------------------------------------------------------------------------- |
| 1     | Foundation               | Domain model, repositories, navigation shell, collection tree, empty state         |
| 2     | REST Execution           | Request builder, HTTP executor, response viewer, basic `{{variable}}` substitution |
| 3     | Environments and Secrets | Full env/variable system, Key Vault secret resolver, active-env switcher           |
| 4     | Authentication           | Bearer, API Key, Basic, OAuth 2 (client credentials + auth code)                   |
| 5     | GraphQL                  | Query editor, schema introspection, autocomplete, subscriptions (`graphql-ws`)     |
| 6     | WebSocket                | Connect/send/listen terminal, virtualized message log                              |
| 7     | Export/Import            | SwebKit-native, Postman v2.1, Bruno export, full-bundle + standalone integration   |
| 8     | Performance and Polish   | Monaco lazy load, virtual scroll, search/filter, keyboard shortcuts, history       |
| 9     | Git-Linked Collections   | SwebKit-owned folder format, linked repo roots, safe Git status/actions            |

## Dependencies

| Dependency                              | Usage                                                                                |
| --------------------------------------- | ------------------------------------------------------------------------------------ |
| `ConfigurationBundleService`            | Phase 7 — extend to include `collections.json` + `environments.json`                 |
| `AppDataFileStore`                      | Phase 1 — `CollectionRepository` and `EnvironmentRepository` use it for file I/O     |
| `ICredentialStore`                      | Phases 3–4 — auth tokens and KV access tokens; never stored in plain collection JSON |
| Monaco Editor (JS)                      | Phase 2+ — existing YAML interop extended; lazy-load on `/api-client` only           |
| `Azure.Security.KeyVault.Secrets` NuGet | Phase 3 — `AzureKeyVaultSecretResolver` in `SwebKit.Azure/`                          |
| `Microsoft.Maui.Authentication`         | Phase 4 — `WebAuthenticator` for OAuth 2 auth code flow                              |
| Git CLI                                 | Phase 9 — optional local status/branch/commit/push actions for linked roots          |

## Risks

| Risk                                                | Mitigation                                                                                         |
| --------------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| Monaco bundle size impacts boot time                | Lazy `import()` inside `apiClientEditor.js`; load only on first `/api-client` visit                |
| OAuth 2 redirect URI scheme unresolved              | OPEN QUESTION — see `decisions.md` PENDING-1; defer to Phase 4 implementation start                |
| Postman v2.1 format complexity                      | Import a focused subset (folders, requests, headers, body, basic auth); document gaps              |
| Bruno `.bru` format is partially documented         | Export-only Phase 7; import deferred to follow-up after format is confirmed stable                 |
| GraphQL introspection adds latency                  | Cache schema per endpoint in page-scoped service; invalidate on URL change                         |
| GraphQL subscriptions need `graphql-ws`             | `graphql-ws` framing implemented on top of `IWebSocketClientService`; no extra NuGet needed        |
| Key Vault unavailable at variable resolution        | Graceful degradation: return `[KV_UNAVAILABLE]` placeholder, never throw                           |
| Large collections degrade UI                        | `<Virtualize>` in collection tree from Phase 1; flattened-list rendering model                     |
| Response body size                                  | Cap display at 500 KB; [Load full response] for larger payloads                                    |
| Post-request JSONPath capture on malformed response | `PostRequestCaptureExecutor` wraps each rule in try/catch; failed rules log a warning, never throw |
| Linked root overwrites external edits               | Track file hash/last-write metadata; prompt before overwriting changed files on disk               |
| Git actions accidentally touch unrelated repo files | Scope status/commit actions to the configured SwebKit API root path only                           |
| Secret values leak into Git-tracked files           | Store only secret references; resolve values from `ICredentialStore` or Key Vault at send time     |

## Quick Links

- Architecture: [docs/architecture/architecture.md](../../../architecture/architecture.md)
- Codebase guide: [docs/architecture/codebase-guide.md](../../../architecture/codebase-guide.md)
- Settings and bundle: [docs/architecture/functionalities/settings-and-configuration.md](../../../architecture/functionalities/settings-and-configuration.md)
- Phase 9 module: [git-linked-collections.md](git-linked-collections.md)
- Jira: not linked
