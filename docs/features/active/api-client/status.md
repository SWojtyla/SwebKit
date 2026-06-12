# Status — API Client

## Current State

`In Progress`

## Current Focus

Phase 3 (Environments and Secrets) complete. Ready for Phase 4 (Authentication).

## Progress Checklist

### Phase 1 — Foundation

- [x] `ApiClientModels.cs` — full domain model in `SwebKit.Core/Domain/`
- [x] `CollectionRepository` — atomic write + `.bak` recovery, `collections.json` via `AppDataFileStore`
- [x] `EnvironmentRepository` — atomic write + `.bak` recovery, `environments.json`
- [x] `UserSettingsRepository` extended: `AutoSaveRequests: bool` (default: `false`)
- [x] Both collection/environment repositories registered in `MauiProgram.cs`
- [x] Route `/api-client` wired in `Routes.razor`
- [x] Sidebar entry + "Tools" nav group in `ShellNavigation.cs` / `LeftNav.razor`
- [x] `src/SwebKit.App/Components/ApiClient/` subfolder created
- [x] `_Imports.razor` updated with `@using SwebKit.App.Components.ApiClient` (BL-1 guard)
- [x] `ApiClientPage.razor` — two-panel layout: collection tree left | request builder+response right
      (single-request focus model — one request open at a time)
- [x] `CollectionTree.razor` — `<Virtualize>` over flattened node list, expand/collapse state
- [x] `RequestQuickNavPanel.razor` — collapsible overlay panel; `Ctrl+P` to focus; click a row to open
- [x] [+ New Request] and [+ New Collection] buttons with keyboard shortcuts
- [x] Auto-save debounce (500 ms) + dirty indicator when `AutoSaveRequests` is `true`
- [x] Empty state component with actionable prompt
- [x] **Nav collapse UX** — left nav auto-collapses to icon-only on entering API client, restores on exit
      (see `decisions.md` for rationale)
- [x] Unit tests — `CollectionRepositoryTests` (14 tests), `EnvironmentRepositoryTests` (14 tests),
      `ApiClientModelsTests` + `UserSettingsAutoSaveTests` (16 tests) — all 44 passing

### Phase 2 — REST Execution

- [x] `HttpRequestResult.cs` in `SwebKit.Core/Domain/` (4 MB truncation, `ResponseBodyTruncated`)
- [x] `IHttpRequestExecutor` contract in `SwebKit.Core/Abstractions/`
- [x] `IVariableSubstitutionService` + `IVariablePreviewService` contracts
- [x] `VariableSubstitutionService` — collection vars → env vars → `ICredentialStore`
- [x] `VariablePreviewService` — secrets masked as `••••••••`; null for unresolved tokens
- [x] `HttpRequestExecutor` — named `IHttpClientFactory("ApiClient")`, 4 MB `LimitedStream`, all body modes
- [x] `UserSettings.VerifyApiClientSsl: bool` (default: `true`)
- [x] Named `HttpClient` + services registered in `MauiProgram.cs`; SSL bypass when setting is `false`
- [x] `KeyValueGrid.razor` — reusable editable key/value grid (headers, query params, form-data)
- [x] `RequestBuilderPanel.razor` — method selector, URL bar, tab strip: Params / Headers / Body / Auth / Capture
- [x] `ResponseViewerPanel.razor` — status badge, timing, size; Body / Headers / Raw tabs
- [x] `ApiClientPage.razor` wired up — left/right splitter with JS drag-resize
- [x] Unit tests — 21 passing

### Phase 3 — Environments and Secrets ✅

- [x] `CollectionVariable.IsEnabled` property added
- [x] `AppConfig.KeyVaultUrl` added
- [x] `HttpRequestResult.CaptureWarnings` added
- [x] `IKeyVaultSecretResolver` contract in `SwebKit.Core/Abstractions/`
- [x] `IPostRequestCaptureExecutor` contract in `SwebKit.Core/Abstractions/`
- [x] `IVariableSubstitutionService.BuildScopeAsync` added (KV resolution)
- [x] `NoopKeyVaultSecretResolver` — returns `[KV_UNAVAILABLE:name]` when no vault configured
- [x] `AzureKeyVaultSecretResolver` in `SwebKit.Azure/` with `DefaultAzureCredential`
- [x] `VariableSubstitutionService` updated: `IsEnabled` check on collection vars; `BuildScopeAsync` for KV vars
- [x] `PostRequestCaptureExecutor` — JSONPath (`JsonPath.Net`), header, status code extraction; upserts to collection or environment variable; warnings on no-match
- [x] `HttpRequestExecutor` updated: uses `BuildScopeAsync`; calls `PostRequestCaptureExecutor` after response
- [x] `MauiProgram.cs` — registers `IPostRequestCaptureExecutor`, `IKeyVaultSecretResolver` (noop vs real based on config)
- [x] `EnvironmentManagerPanel.razor` + `EnvironmentEditor.razor` — full CRUD, variable grid with type selector
- [x] `CollectionVariableEditor.razor` — key/value grid with IsEnabled toggle
- [x] `PostRequestCaptureBuilder.razor` — source type dropdown, JSONPath/header expression, target var + scope
- [x] `RequestBuilderPanel` — "Capture" tab added, `CaptureWarnings` passed after execution, `AllEnvironments` parameter
- [x] `ApiClientPage` — overlay panels for environment manager + collection var editor; toolbar buttons
- [x] Unit tests — `PostRequestCaptureExecutorTests` (10 tests) + `VariableSubstitutionServicePhase3Tests` (4 tests) + `NoopKeyVaultSecretResolverTests` (2 tests) — 16 new tests
- [x] Total: 540 tests passing, build clean (pre-existing MSIX signing error only)

**Variable scope:**

- [ ] `Collection.CollectionVariables: List<CollectionVariable>` — always-active key/value pairs,
      no environment required; plain values or `ICredentialStore` references
- [ ] `VariableSubstitutionService` updated: resolve collection vars first, then env vars;
      env vars override collection vars when an environment is active
- [ ] `IVariablePreviewService` updated to reflect the same resolution chain

**Environment manager:**

- [ ] `EnvironmentManagerPanel.razor` — list environments, add/edit/delete
- [ ] `EnvironmentEditor.razor` — variable grid: key | type (Plain / SecretStore / KeyVault) | value
- [ ] Secret type: value masked; stored in `ICredentialStore`
- [ ] Key Vault type: KV secret name input; resolved at execution time via `DefaultAzureCredential`
- [ ] `IKeyVaultSecretResolver` contract in `SwebKit.Core/Abstractions/`
- [ ] `AzureKeyVaultSecretResolver` in `SwebKit.Azure/` using `Azure.Security.KeyVault.Secrets`
      with `DefaultAzureCredential`
- [ ] KV setup prerequisite guard in Settings (new `KeyVaultUrl` in `AppConfig`)
- [ ] Active environment switcher in `ApiClientPage` toolbar
- [ ] Variable preview badge updated to show collection-var vs env-var origin (tooltip)
- [ ] [Test resolution] button in `EnvironmentEditor`

**Collection variable editor:**

- [ ] `CollectionVariableEditor.razor` — accessible from right-click menu on collection node;
      grid of key/value pairs; separate from environment variables

**Post-request capture rules:**

- [ ] `CaptureRule` model: `CaptureSourceType` (JsonPath, Header, StatusCode), `SourceExpression`,
      `TargetVariableName`, `TargetScope` (Collection or Environment)
- [ ] `IPostRequestCaptureExecutor` contract in `SwebKit.Core/Abstractions/`
- [ ] `PostRequestCaptureExecutor` implementation in `SwebKit.Core/Services/`:
      applies rules sequentially; per-rule try/catch; failed rules add a capture warning to result;
      JSONPath evaluated with `JsonPath.Net`; mutates `CollectionRepository` or
      `EnvironmentRepository` on successful extraction
- [ ] `HttpRequestExecutor` calls `IPostRequestCaptureExecutor` after receiving the response
- [ ] `PostRequestCaptureBuilder.razor` — visual block list below the response:
      [+ Add Capture] opens a row: source type selector | expression input | → variable name |
      scope selector; [Test capture] re-evaluates the last response with all rules
- [ ] Capture warnings shown in `ResponseViewerPanel` when one or more rules failed to match

### Phase 4 — Authentication

- [ ] `Collection.DefaultAuth: AuthConfig?` and `RequestFolder.DefaultAuth: AuthConfig?` added
      to domain model
- [ ] `IAuthInheritanceResolver` contract + `AuthInheritanceResolver` implementation:
      walks `HttpRequestEntry.Auth → RequestFolder.DefaultAuth → Collection.DefaultAuth`;
      returns first non-null auth config
- [ ] Auth tab wired in `RequestBuilderPanel.razor`; shows resolved/inherited auth with
      an "Inherited from [folder/collection name]" badge when request auth is null
- [ ] `BearerAuthForm.razor` — token input (password-masked; `ICredentialStore` backed)
- [ ] `ApiKeyAuthForm.razor` — key name, value, placement radio (Header / Query Param)
- [ ] `BasicAuthForm.razor` — username + password (masked)
- [ ] `OAuth2AuthForm.razor` — flow selector, token URL, client ID, scopes, [Get Token] button
      (redirect URI scheme: **OPEN QUESTION** — see `decisions.md` PENDING-1)
- [ ] Client credentials flow: token endpoint POST via `HttpClient`
- [ ] Auth code flow: `WebAuthenticator.AuthenticateAsync` with PKCE
- [ ] `OAuth2TokenManager` — in-memory token cache + expiry refresh (re-fetch 60 s before expiry)
- [ ] Refresh token stored in `ICredentialStore`; auto-used when access token expires (auth code flow)
- [ ] Auth never serialised to `collections.json` — only `CredentialKey` reference stored
- [ ] `IAuthInheritanceResolver` registered as `Scoped` in `MauiProgram.cs`

### Phase 5 — GraphQL

- [ ] `GRAPHQL` pseudo-method in method selector
- [ ] Query editor pane (Monaco `graphql` mode)
- [ ] Variables editor pane (Monaco `json` mode)
- [ ] Operation selector: parse document for named `query`/`mutation`/`subscription` operations;
      show a dropdown above the editor when more than one operation is found;
      selected operation name sent in the request body as `operationName`
- [ ] [Introspect Schema] button — `__schema` query + cache per endpoint;
      on introspection error: show dismissible warning banner above the editor,
      do NOT block editing or sending
- [ ] Schema-aware autocomplete via `monaco-graphql` plugin
- [ ] GraphQL error rendering in `ResponseViewerPanel` (distinct from HTTP errors;
      `errors` array surfaced as a separate tab)
- [ ] **Subscriptions:** detect `subscription` keyword as the selected operation;
      switch to `graphql-ws` WebSocket connection automatically: - `IGraphQlSubscriptionService` contract in `SwebKit.Core/Abstractions/` - `GraphQlSubscriptionService` in `SwebKit.Core/Services/` — wraps `IWebSocketClientService`
      with `graphql-ws` framing (`connection_init` / `subscribe` / `next` / `complete`) - Subscription messages stream into a virtualized `ResponseViewerPanel`
      (same `WebSocketMessage` direction model as Phase 6) - [Stop subscription] button visible while subscription is active

### Phase 6 — WebSocket

- [ ] `IWebSocketClientService` contract + `WebSocketClientService` (`ClientWebSocket` wrapper)
- [ ] `ConnectAsync` accepts `IReadOnlyList<HeaderEntry>` for custom upgrade headers including
      `Sec-WebSocket-Protocol` (subprotocol configurable from the Headers tab)
- [ ] Receive loop posts to `Channel<WebSocketMessage>`; capped at 10 000 frames
      (oldest frame dropped silently when cap is reached)
- [ ] `IAsyncDisposable` cleanup on navigation away
- [ ] `WebSocketEntry` extended with `List<SavedMessage> SavedMessages`
      (`SavedMessage`: name, content string)
- [ ] `WebSocketPanel.razor` — URL input, Headers tab (for `Sec-WebSocket-Protocol` etc.),
      connect/disconnect, message composer with [Text/Binary] type selector,
      saved message templates dropdown, live message log
- [ ] Message log virtualized; messages timestamped with direction indicator (↑/↓)
- [ ] Binary frame support (hex display)
- [ ] [Save message] button in composer creates a named template on `WebSocketEntry.SavedMessages`
- [ ] [Clear log] button
- [ ] Connection state badge: Disconnected (grey) / Connecting (yellow) / Connected (green) / Faulted (red)

### Phase 7 — Export/Import

- [ ] `ICollectionExporter` / `ICollectionImporter` contracts in `SwebKit.Core/Abstractions/`
- [ ] `SwebKitCollectionExporter` + `SwebKitCollectionImporter` (versioned JSON; lossless round-trip)
- [ ] `PostmanCollectionExporter` — Postman Collection v2.1 output
- [ ] `PostmanCollectionImporter` — Postman v2.1 subset parse;
      **extract collection variables as a new `ApiEnvironment`** named `"<CollectionName> (imported)"`
- [ ] `IEnvironmentImporter` contract + `SwebKitEnvironmentImporter` (standalone environment JSON)
- [ ] `BrunoCollectionExporter` — `.bru` per-request files zipped
- [ ] Name collision on import: auto-rename to "Name (2)", "Name (3)" etc. — never overwrite silently
- [ ] `ConfigurationBundleService` extended to include `collections.json` + `environments.json`
- [ ] `ConfigurationBundleModels` extended with nullable `CollectionsData` and `EnvironmentsData`
      (backward-compatible — bundles without these fields restore cleanly)
- [ ] `CollectionExportDialog.razor` — format selector, include-environments checkbox, file save/open
- [ ] [Import Environment] button in dialog (standalone environment file import)
- [ ] Format auto-detection on import (by file extension and magic bytes)
- [ ] Import summary panel: X requests imported, Y capture rules imported,
      Z auth configs requiring re-entry, N variables extracted as environment

### Phase 8 — Performance and Polish

- [ ] Monaco lazy load — dynamic `import()` on first `/api-client` route activation
- [ ] Collection search/filter bar in `CollectionTree.razor` (searches across all collections)
- [ ] Request history sidebar — last 20 responses per request in-memory (lost on restart);
      click a history entry to view the response; [Re-send] loads it back into the builder
- [ ] Keyboard shortcuts registered in `CommandRegistry.cs`:
      `Ctrl+Enter` send, `Ctrl+N` new request, `Ctrl+Shift+N` new collection,
      `Ctrl+E` env manager, `Ctrl+P` quick-nav panel, `Escape` cancel
- [ ] Response body truncation enforced at 500 KB + [Load full response] affordance
- [ ] Functional docs entry for `/api-client` in `docs/architecture/functionalities/`
- [ ] Drag-and-drop reordering: **explicitly deferred to post-Phase-8 follow-up** —
      not a Phase 8 gate condition

## Completed Work

_Nothing yet._

## Blockers

_None._

## Validation Status

Not started.
