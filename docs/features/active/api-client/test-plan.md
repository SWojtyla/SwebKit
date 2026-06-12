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
