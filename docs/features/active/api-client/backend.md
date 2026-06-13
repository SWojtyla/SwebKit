# Backend — API Client

## Current State

The API Client backend lives mostly in `SwebKit.Core`, with UI orchestration and Windows-specific secret storage in `SwebKit.App`, and Key Vault resolution in `SwebKit.Azure`.

The current model is class-based mutable state optimized for Blazor editing and JSON persistence. Earlier record-based proposal shapes are superseded by `src/SwebKit.Core/Domain/ApiClientModels.cs` and `src/SwebKit.Core/Domain/LinkedCollectionModels.cs`.

## Domain Model

| Type                                       | Purpose                                                                                             |
| ------------------------------------------ | --------------------------------------------------------------------------------------------------- |
| `ApiCollection`                            | Top-level named collection with nodes, collection variables, and optional default auth              |
| `ApiCollectionNode`                        | Folder or request tree node; folders can carry child nodes and default auth                         |
| `HttpRequestEntry`                         | REST, GraphQL, or WebSocket request definition                                                      |
| `RequestBody`                              | Body mode and raw/form/binary content metadata                                                      |
| `AuthConfig`                               | None/inherited/Bearer/API key/Basic/OAuth 2 auth config; secret values remain in `ICredentialStore` |
| `CaptureRule`                              | No-code post-request capture definition                                                             |
| `ApiEnvironment` / `EnvironmentVariable`   | Environment variable set with plain, Windows Credential Store, Key Vault, or generated variables    |
| `VariableGeneratorDefinition`              | Safe generated variable definition for primitive, faker, list, and template values                  |
| `VariableInspectionItem`                   | Source/value metadata for request variable inspector rows                                           |
| `ResponseExample`                          | Saved response example attached to a request                                                        |
| `CollectionRunItemResult`                  | Per-request collection runner result                                                                |
| `LinkedCollectionRootConfig`               | User-local linked root registration stored in `api-linked-roots.json`                               |
| `LinkedGitStatus` / `LinkedGitChangedFile` | Scoped Git status for files under a linked API root                                                 |
| `LinkedGitFileDiff`                        | Original/current text payload for in-app linked API file review                                     |

## App-Local Persistence

| Store                         | File                                      | Repository                       |
| ----------------------------- | ----------------------------------------- | -------------------------------- |
| Collections                   | `%APPDATA%/SwebKit/collections.json`      | `CollectionRepository`           |
| Environments and API UI state | `%APPDATA%/SwebKit/environments.json`     | `EnvironmentRepository`          |
| Linked root registrations     | `%APPDATA%/SwebKit/api-linked-roots.json` | `LinkedCollectionRootRepository` |

Repositories use the existing `AppDataFileStore` atomic write and `.bak` recovery pattern. Local collections are separate from linked-root collections; linked files are loaded from disk and treated as their own source of truth.

## Linked Root Folder Format

Linked API roots use `.swebkit-api/` under a user-selected repository folder:

```text
.swebkit-api/
  swebkit.json
  collections/
    orders/
      collection.json
      get-order.swebreq.json
      create-order.body.json
  environments/
    dev.swebenv.json
```

`LinkedCollectionFileService` owns:

- root creation and manifest discovery
- compact request file read/write with default inference
- body/query/variables sidecar handling
- linked environment read/write
- generated variable sections in collection manifests and environment files
- request content stamps for external-change conflict detection
- saved response example read/write on request files
- linked collection folder creation

Linked files store secret references only. Secret values are resolved from Windows Credential Store or Key Vault at send time.

## Git Integration

`LinkedGitService` shells out to the installed Git CLI with fixed argument builders. It never accepts arbitrary Git command text.

Implemented operations:

- detect repository root, current branch, and changed API files
- list local branches for dropdown switching
- create branch
- switch branch when linked API root is clean unless explicitly allowed by caller
- stage and unstage changed API files under the linked API root
- revert changed API files under the linked API root
- load original/current text for in-app diff preview of changed API files
- read origin remote URL for commit preview context
- commit staged API files while rejecting unrelated staged files outside the API root
- commit all API-root changes through the legacy scoped commit helper
- push current branch
- infer GitHub and Azure DevOps remote compare URLs

Guardrails:

- status and changed file lists are filtered to the configured API root
- commits from the staged flow reject non-API staged files
- revert operates only on changed files already reported under the API root
- pull, rebase, stash, arbitrary command execution, and PR creation are out of scope

## Request Execution

`HttpRequestExecutor` uses the named `HttpClient` `ApiClient` registered in `MauiProgram.cs`.

Execution flow:

1. Build variable scope through `IVariableSubstitutionService.BuildScopeAsync`.
2. Substitute URL, headers, query params, body, and GraphQL fields.
3. Resolve and apply inherited or request-level auth through `IAuthInheritanceResolver` and `IAuthHeaderBuilder`.
4. Send HTTP request with the named client.
5. Parse GraphQL errors when applicable.
6. Run post-request capture rules through `IPostRequestCaptureExecutor`.
7. Return `HttpRequestResult` to the UI.

Response display caps are enforced in the UI at 500 KB. The executor keeps the wire cap/truncation contract for large responses.

## Variables, Secrets, and Generated Values

`VariableSubstitutionService` merges enabled collection variables and active environment variables. Environment values override collection values with the same key.

Supported values:

- plain string values
- Windows Credential Store values via `ICredentialStore`
- Azure Key Vault values via `IKeyVaultSecretResolver`
- generated values through `IVariableGeneratorService`

`VariableGeneratorService` supports:

- integer range
- decimal range
- boolean with true-weight percentage
- GUID
- UTC date/time
- list pick
- Bogus-backed faker categories: first name, last name, full name, email, phone, company
- template composition using existing scope values

Generated sample values are never persisted; only `VariableGeneratorDefinition` is stored.

`VariablePreviewService` returns masked values for secret-like token names and `null` for unresolved tokens. The post-Phase-10 roadmap adds a richer variable inspector that should expose source and warning metadata, not just preview text.

## Workflow, Portability, and Examples

`ApiClientWorkflowService` owns user-facing workflow helpers that do not belong to request execution itself:

- Copy as cURL with secret-backed values masked by default.
- Import from cURL for method, URL, headers, and raw body.
- Variable inspection with source metadata: collection, environment, generated, credential store, Key Vault, unresolved.
- Response example creation with secret-looking headers and JSON properties masked before persistence.

`ResponseExample` values are stored on `HttpRequestEntry.ResponseExamples`. Linked request files persist examples only after the user explicitly saves them.

## Authentication

Auth can be attached at request, folder, or collection level. `null` request auth means inherit. Explicit `AuthType.None` opts out of inheritance.

Supported auth types:

- none
- inherited
- bearer token
- API key in header or query param
- Basic auth
- OAuth 2 client credentials
- OAuth 2 authorization code with PKCE through MAUI `WebAuthenticator`

OAuth redirect uses `sweb://oauth` per DEC-17. Tokens and client secrets are referenced by credential key; values are not persisted in collection JSON or linked files.

## GraphQL and WebSocket Services

| Service                      | Responsibility                                                                  |
| ---------------------------- | ------------------------------------------------------------------------------- |
| `GraphQlSchemaService`       | Schema introspection, operation parsing, and per-endpoint schema cache          |
| `GraphQlSubscriptionService` | `graphql-ws` protocol framing over `IWebSocketClientService`                    |
| `WebSocketClientService`     | `ClientWebSocket` wrapper with bounded `Channel<WebSocketMessage>` receive pipe |

`IWebSocketClientService` is transient because a WebSocket connection is tied to a panel/request lifetime. `GraphQlSubscriptionService` is transient for the same reason.

## Collection Runner

`ApiClientCollectionRunnerService` executes requests from an `ApiCollection` sequentially through the existing `IHttpRequestExecutor` path. It reports a `CollectionRunItemResult` after each request through an optional callback and returns the full result list at completion.

Runner behavior:

- reuses existing auth, variable substitution, generated values, capture rules, and response caps
- supports cancellation through `CancellationToken`
- skips WebSocket requests with an explicit result row
- catches per-request non-cancellation exceptions and continues to later requests
- does not execute pre-request scripts or arbitrary code

## Export and Import

Implemented formats:

| Format                   | Service                                                   | Direction     | Notes                           |
| ------------------------ | --------------------------------------------------------- | ------------- | ------------------------------- |
| SwebKit JSON             | `SwebKitCollectionExporter` / `SwebKitCollectionImporter` | import/export | Lossless app-owned format       |
| SwebKit environment JSON | `SwebKitEnvironmentImporter`                              | import        | Standalone environment import   |
| Postman v2.1             | `PostmanCollectionExporter` / `PostmanCollectionImporter` | import/export | Focused subset; scripts omitted |
| Bruno                    | `BrunoCollectionExporter`                                 | export        | Zip with one `.bru` per request |

`CollectionImportService` handles format detection, collision-safe naming, and repository persistence. `ConfigurationBundleService` includes collections and environments in full app bundle export/import.

## Dependency Injection Summary

Current API Client registrations in `MauiProgram.cs` include:

- singleton repositories: `CollectionRepository`, `EnvironmentRepository`, `LinkedCollectionRootRepository`
- singleton variable services: `IVariableGeneratorService`, `IVariableSubstitutionService`, `IVariablePreviewService`
- singleton workflow services: `ApiClientWorkflowService`, `ApiClientCollectionRunnerService`
- singleton auth/capture services: `IPostRequestCaptureExecutor`, `IOAuth2TokenManager`, `IAuthHeaderBuilder`, `IAuthInheritanceResolver`
- singleton GraphQL schema and linked-file/Git services: `IGraphQlSchemaService`, `LinkedGitService`, `LinkedCollectionFileService`
- transient runtime connection services: `IHttpRequestExecutor`, `IGraphQlSubscriptionService`, `IWebSocketClientService`
- export/import singletons for SwebKit, Postman, Bruno, and collection import orchestration
- `IKeyVaultSecretResolver` selected from multi-vault config, legacy single URL config, or no-op fallback

## Package Dependencies

| Package                           | Project         | Usage                                     |
| --------------------------------- | --------------- | ----------------------------------------- |
| `Azure.Security.KeyVault.Secrets` | `SwebKit.Azure` | Key Vault secret resolution               |
| `JsonPath.Net`                    | `SwebKit.Core`  | Post-request JSONPath capture             |
| `Bogus`                           | `SwebKit.Core`  | Phase 10 faker-backed generated variables |

## Follow-Up Notes

The post-Phase-10 backend roadmap is implemented. Future backend work should start from a new feature plan, especially for any expansion into assertions, cookie handling, PR creation, or hosted collaboration.
