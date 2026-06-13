# Test Plan — API Client

## Strategy

- **Unit tests** for all domain logic: variable substitution, export/import converters, executor,
  OAuth token management, repository round-trips.
- **bUnit component tests** for UI state: tab switching, collection tree rendering, response viewer
  state, auth forms.
- **Manual/visual** for Monaco integration, drag-and-drop, and performance benchmarks.
- **E2E** deferred — no live HTTP call tests in CI.

---

## Phase 1 — Foundation

| #   | Scenario                                                                                        | Type  | Project              |
| --- | ----------------------------------------------------------------------------------------------- | ----- | -------------------- |
| 1.1 | `CollectionRepository.LoadAsync` reads `collections.json` and returns populated `ApiClientData` | Unit  | `SwebKit.Core.Tests` |
| 1.2 | `CollectionRepository.LoadAsync` falls back to `.bak` when primary file is corrupt              | Unit  | `SwebKit.Core.Tests` |
| 1.3 | `CollectionRepository.SaveAsync` writes atomically (temp-file replace) and refreshes `.bak`     | Unit  | `SwebKit.Core.Tests` |
| 1.4 | `EnvironmentRepository` round-trips named environments including variable lists                 | Unit  | `SwebKit.Core.Tests` |
| 1.5 | `CollectionTree` renders empty state when no collections exist                                  | bUnit | `SwebKit.App.Tests`  |
| 1.6 | `CollectionTree` renders a two-level folder/request hierarchy correctly                         | bUnit | `SwebKit.App.Tests`  |
| 1.7 | `CollectionTree` expand/collapse toggles child visibility                                       | bUnit | `SwebKit.App.Tests`  |
| 1.8 | Route `/api-client` resolves to `ApiClientPage`                                                 | bUnit | `SwebKit.App.Tests`  |

---

## Phase 2 — REST Execution

| #    | Scenario                                                                                     | Type  | Project              |
| ---- | -------------------------------------------------------------------------------------------- | ----- | -------------------- |
| 2.1  | `HttpRequestExecutor.ExecuteAsync` sends GET with correct URL and headers                    | Unit  | `SwebKit.Core.Tests` |
| 2.2  | `HttpRequestExecutor` serialises POST body as raw JSON string                                | Unit  | `SwebKit.Core.Tests` |
| 2.3  | `HttpRequestExecutor` respects `CancellationToken` — throws `OperationCanceledException`     | Unit  | `SwebKit.Core.Tests` |
| 2.4  | `VariableSubstitutionService` replaces all `{{key}}` tokens in URL, headers, and body        | Unit  | `SwebKit.Core.Tests` |
| 2.5  | `VariableSubstitutionService` with no active environment leaves `{{key}}` placeholder intact | Unit  | `SwebKit.Core.Tests` |
| 2.6  | `VariableSubstitutionService` with multiple occurrences of the same key replaces all         | Unit  | `SwebKit.Core.Tests` |
| 2.7  | `RequestBuilderPanel` renders Params tab by default                                          | bUnit | `SwebKit.App.Tests`  |
| 2.8  | `RequestBuilderPanel` switching to Body tab shows Monaco editor container                    | bUnit | `SwebKit.App.Tests`  |
| 2.9  | `ResponseViewerPanel` renders status 200 with green badge                                    | bUnit | `SwebKit.App.Tests`  |
| 2.10 | `ResponseViewerPanel` renders 4xx with orange badge and error label                          | bUnit | `SwebKit.App.Tests`  |
| 2.11 | `ResponseViewerPanel` shows elapsed time and response size                                   | bUnit | `SwebKit.App.Tests`  |

---

## Phase 3 — Environments and Secrets

| #   | Scenario                                                                                        | Type  | Project               |
| --- | ----------------------------------------------------------------------------------------------- | ----- | --------------------- |
| 3.1 | `VariableSubstitutionService` resolves plain environment variable                               | Unit  | `SwebKit.Core.Tests`  |
| 3.2 | `VariableSubstitutionService` resolves `SecretStore`-type variable via `ICredentialStore`       | Unit  | `SwebKit.Core.Tests`  |
| 3.3 | `VariableSubstitutionService` returns `[KV_UNAVAILABLE]` when KV resolver fails                 | Unit  | `SwebKit.Core.Tests`  |
| 3.4 | `AzureKeyVaultSecretResolver` returns resolved secret value for known secret name               | Unit  | `SwebKit.Azure.Tests` |
| 3.5 | `AzureKeyVaultSecretResolver` returns `null` (not throw) when KV is unreachable                 | Unit  | `SwebKit.Azure.Tests` |
| 3.6 | `EnvironmentEditor` masks secret-type variable values in the grid                               | bUnit | `SwebKit.App.Tests`   |
| 3.7 | Active environment switch in toolbar triggers re-resolve of variable preview in request builder | bUnit | `SwebKit.App.Tests`   |

---

## Phase 4 — Authentication

| #   | Scenario                                                                                  | Type | Project              |
| --- | ----------------------------------------------------------------------------------------- | ---- | -------------------- |
| 4.1 | Bearer token is injected as `Authorization: Bearer <token>` header                        | Unit | `SwebKit.Core.Tests` |
| 4.2 | API Key (header) is injected as a custom request header                                   | Unit | `SwebKit.Core.Tests` |
| 4.3 | API Key (query param) is appended to the URL                                              | Unit | `SwebKit.Core.Tests` |
| 4.4 | Basic auth is base64-encoded as `Authorization: Basic <encoded>`                          | Unit | `SwebKit.Core.Tests` |
| 4.5 | OAuth 2 client credentials flow returns token and stores it in `OAuth2TokenManager` cache | Unit | `SwebKit.Core.Tests` |
| 4.6 | `OAuth2TokenManager` re-fetches token when cached token is expired                        | Unit | `SwebKit.Core.Tests` |
| 4.7 | `AuthConfig` serialised to JSON contains only `CredentialKey`, not the secret value       | Unit | `SwebKit.Core.Tests` |

---

## Phase 5 — GraphQL

| #   | Scenario                                                                                      | Type  | Project              |
| --- | --------------------------------------------------------------------------------------------- | ----- | -------------------- |
| 5.1 | GraphQL request serialises query + variables as `{ "query": "...", "variables": {...} }` body | Unit  | `SwebKit.Core.Tests` |
| 5.2 | Schema introspection result is cached; second call returns cached value without HTTP          | Unit  | `SwebKit.Core.Tests` |
| 5.3 | Schema cache is invalidated when endpoint URL changes                                         | Unit  | `SwebKit.Core.Tests` |
| 5.4 | `ResponseViewerPanel` surfaces GraphQL `errors` array distinctly from HTTP 4xx                | bUnit | `SwebKit.App.Tests`  |

---

## Phase 6 — WebSocket

| #   | Scenario                                                                                       | Type  | Project              |
| --- | ---------------------------------------------------------------------------------------------- | ----- | -------------------- |
| 6.1 | `WebSocketClientService.ConnectAsync` transitions state to `Connected`                         | Unit  | `SwebKit.Core.Tests` |
| 6.2 | `WebSocketClientService.SendAsync` calls underlying socket send                                | Unit  | `SwebKit.Core.Tests` |
| 6.3 | `WebSocketClientService.DisconnectAsync` transitions state to `Disconnected` without exception | Unit  | `SwebKit.Core.Tests` |
| 6.4 | `DisposeAsync` closes the underlying socket even if not explicitly disconnected                | Unit  | `SwebKit.Core.Tests` |
| 6.5 | `WebSocketPanel` renders message log with sent/received direction indicators                   | bUnit | `SwebKit.App.Tests`  |
| 6.6 | [Clear log] button empties the message list                                                    | bUnit | `SwebKit.App.Tests`  |

---

## Phase 7 — Export/Import

| #    | Scenario                                                                                   | Type | Project              |
| ---- | ------------------------------------------------------------------------------------------ | ---- | -------------------- |
| 7.1  | SwebKit-native round-trip: export then import produces structurally identical `Collection` | Unit | `SwebKit.Core.Tests` |
| 7.2  | SwebKit export includes schema version field `"version": "SwebKitCollectionV1"`            | Unit | `SwebKit.Core.Tests` |
| 7.3  | Postman v2.1 export produces valid top-level schema shape (`info`, `item` array)           | Unit | `SwebKit.Core.Tests` |
| 7.4  | Postman v2.1 import maps folder names, request name, method, URL, and headers              | Unit | `SwebKit.Core.Tests` |
| 7.5  | Postman import ignores `event` (test scripts) without error                                | Unit | `SwebKit.Core.Tests` |
| 7.6  | Bruno export zip contains one `.bru` file per request with correct request syntax          | Unit | `SwebKit.Core.Tests` |
| 7.7  | Full-bundle export payload includes `collectionsData` and `environmentsData` fields        | Unit | `SwebKit.Core.Tests` |
| 7.8  | Full-bundle import restores collections without overwriting unrelated profile data         | Unit | `SwebKit.Core.Tests` |
| 7.9  | Standalone collection export file can be re-imported to reproduce the same collection      | Unit | `SwebKit.Core.Tests` |
| 7.10 | `AuthConfig.CredentialKey` values are preserved in export; actual secret values are absent | Unit | `SwebKit.Core.Tests` |

---

## Phase 8 — Performance and Polish

| #   | Scenario                                                                           | Type   | Project             |
| --- | ---------------------------------------------------------------------------------- | ------ | ------------------- |
| 8.1 | Collection search/filter returns only matching request names                       | bUnit  | `SwebKit.App.Tests` |
| 8.2 | Collection search with empty query shows all requests                              | bUnit  | `SwebKit.App.Tests` |
| 8.3 | Monaco is NOT initialised on app boot — only on first `/api-client` visit (manual) | Manual | —                   |
| 8.4 | Collection tree with 500 requests renders without visible lag (manual)             | Manual | —                   |
| 8.5 | Ctrl+Enter triggers Send in `RequestBuilderPanel`                                  | bUnit  | `SwebKit.App.Tests` |

---

## Phase 9 — Git-Linked Collections

| #    | Scenario                                                                                | Type   | Project              |
| ---- | --------------------------------------------------------------------------------------- | ------ | -------------------- |
| 9.1  | Linked root manifest loads from `.swebkit-api/swebkit.json`                             | Unit   | `SwebKit.Core.Tests` |
| 9.2  | Compact request file infers defaults for name, id, empty headers, empty query, and auth | Unit   | `SwebKit.Core.Tests` |
| 9.3  | Request with `jsonFile`, `queryFile`, or `variablesFile` loads sibling sidecar content  | Unit   | `SwebKit.Core.Tests` |
| 9.4  | Writer serializes deterministic JSON and omits default/null fields                      | Unit   | `SwebKit.Core.Tests` |
| 9.5  | Writer never persists secret values, only secret references                             | Unit   | `SwebKit.Core.Tests` |
| 9.6  | External file changes are detected before overwriting a linked request                  | Unit   | `SwebKit.Core.Tests` |
| 9.7  | Invalid manifest/request files produce diagnostics without hiding the whole root        | Unit   | `SwebKit.Core.Tests` |
| 9.8  | Collection tree renders Local Collections and Linked Repositories as separate groups    | bUnit  | `SwebKit.App.Tests`  |
| 9.9  | Linked root header shows branch and clean/dirty status when Git metadata is available   | bUnit  | `SwebKit.App.Tests`  |
| 9.10 | Missing linked secret shows a configure-secret affordance and blocks only affected send | bUnit  | `SwebKit.App.Tests`  |
| 9.11 | Git command builder scopes status/commit file paths to the configured API root only     | Unit   | `SwebKit.Core.Tests` |
| 9.12 | Manual: open two Git repos with linked roots, edit one request in each, commit and push | Manual | —                    |
| 9.13 | Linked environment file loads plain variables and secret references                     | Unit   | `SwebKit.Core.Tests` |
| 9.14 | Linked environment save writes secret references, not secret values                     | Unit   | `SwebKit.Core.Tests` |
| 9.15 | Remote compare helper infers a GitHub compare URL from origin + current branch          | Unit   | `SwebKit.Core.Tests` |
| 9.16 | Selecting a linked root targets new collection creation to that `.swebkit-api` root     | Unit   | `SwebKit.Core.Tests` |
| 9.17 | Git branch list includes the current branch for dropdown switching                      | Unit   | `SwebKit.Core.Tests` |
| 9.18 | Stage, unstage, and revert operations affect only changed files under the API root      | Unit   | `SwebKit.Core.Tests` |
| 9.19 | Staged commit rejects unrelated staged files outside the API root                       | Unit   | `SwebKit.Core.Tests` |

---

## Phase 10 — Dynamic Variables

| #    | Scenario                                                                   | Type  | Project              |
| ---- | -------------------------------------------------------------------------- | ----- | -------------------- |
| 10.1 | Integer generator produces values inside inclusive min/max constraints     | Unit  | `SwebKit.Core.Tests` |
| 10.2 | Invalid generator constraints return warnings and leave token unresolved   | Unit  | `SwebKit.Core.Tests` |
| 10.3 | Faker first/last name generators produce non-empty values                  | Unit  | `SwebKit.Core.Tests` |
| 10.4 | Template generator composes generated and plain variables                  | Unit  | `SwebKit.Core.Tests` |
| 10.5 | Generated variable definitions serialize without storing generated samples | Unit  | `SwebKit.Core.Tests` |
| 10.6 | Variable preview shows generated sample values and supports refresh        | bUnit | `SwebKit.App.Tests`  |
| 10.7 | Environment/collection variable editor shows building-block fields by kind | bUnit | `SwebKit.App.Tests`  |

---

## Phase 11 — Workflow Trust and Git Review

| #    | Scenario                                                                                         | Type   | Project              |
| ---- | ------------------------------------------------------------------------------------------------ | ------ | -------------------- |
| 11.1 | Toolbar target chip identifies local collection vs selected linked repository                    | bUnit  | `SwebKit.App.Tests`  |
| 11.2 | Git diff preview loads original and modified API file content for changed linked-root files      | Unit   | `SwebKit.Core.Tests` |
| 11.3 | Git panel separates staged and unstaged API files                                                | bUnit  | `SwebKit.App.Tests`  |
| 11.4 | Commit preview includes branch, staged API files, and remote target without unrelated repo files | bUnit  | `SwebKit.App.Tests`  |
| 11.5 | Conflict prompt exposes Reload from disk, Keep mine, and Save as copy actions                    | bUnit  | `SwebKit.App.Tests`  |
| 11.6 | Manual: external file edit produces conflict actions rather than a passive save error            | Manual | —                    |

---

## Phase 12 — Request Portability and Variable Clarity

| #    | Scenario                                                                                 | Type  | Project              |
| ---- | ---------------------------------------------------------------------------------------- | ----- | -------------------- |
| 12.1 | Copy as cURL serializes method, URL, query, headers, and body for REST requests          | Unit  | `SwebKit.Core.Tests` |
| 12.2 | Copy as cURL masks secret-backed values by default                                       | Unit  | `SwebKit.Core.Tests` |
| 12.3 | Import from cURL creates a request with method, URL, headers, query, and body            | Unit  | `SwebKit.Core.Tests` |
| 12.4 | Variable inspector lists token source: environment, collection, generated, secret, or KV | bUnit | `SwebKit.App.Tests`  |
| 12.5 | Variable inspector marks unresolved tokens without blocking unrelated request editing    | bUnit | `SwebKit.App.Tests`  |

---

## Phase 13 — Workspace Depth and Response Examples

| #    | Scenario                                                                                 | Type   | Project              |
| ---- | ---------------------------------------------------------------------------------------- | ------ | -------------------- |
| 13.1 | Pinned request tabs preserve dirty state independently per request                       | bUnit  | `SwebKit.App.Tests`  |
| 13.2 | Switching pinned requests preserves response/subscription state without cross-talk       | bUnit  | `SwebKit.App.Tests`  |
| 13.3 | Saved response example serializes status, headers, body, timestamp, and environment name | Unit   | `SwebKit.Core.Tests` |
| 13.4 | Saved response examples do not persist secret-backed values                              | Unit   | `SwebKit.Core.Tests` |
| 13.5 | Manual: pinned tabs restore predictably according to the chosen persistence model        | Manual | —                    |

---

## Phase 14 — Collection Runner Later

| #    | Scenario                                                                              | Type   | Project              |
| ---- | ------------------------------------------------------------------------------------- | ------ | -------------------- |
| 14.1 | Runner executes folder requests sequentially using existing request execution path    | Unit   | `SwebKit.Core.Tests` |
| 14.2 | Runner cancellation stops remaining requests and preserves completed results          | Unit   | `SwebKit.Core.Tests` |
| 14.3 | Runner result view shows per-request status, elapsed time, size, and capture warnings | bUnit  | `SwebKit.App.Tests`  |
| 14.4 | Runner does not add pre-request scripts or arbitrary code execution                   | Review | —                    |
| 14.5 | Manual: one failed request does not corrupt later request state or captured variables | Manual | —                    |
