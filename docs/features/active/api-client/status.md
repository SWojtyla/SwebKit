# Status — API Client

## Current State

`Planned`

## Current Focus

Phase 1 — Foundation (not yet started)

## Progress Checklist

### Phase 1 — Foundation
- [ ] `ApiClientModels.cs` — `Collection`, `RequestFolder`, `HttpRequestEntry`, `ApiEnvironment`,
      `EnvironmentVariable`, `AuthConfig` in `SwebKit.Core/Domain/`
- [ ] `CollectionRepository` — atomic write + `.bak` recovery, `collections.json` via `AppDataFileStore`
- [ ] `EnvironmentRepository` — atomic write + `.bak` recovery, `environments.json`
- [ ] Both repositories registered in `MauiProgram.cs`
- [ ] Route `/api-client` wired in `Routes.razor`
- [ ] Sidebar entry in `LeftNav.razor`
- [ ] `src/SwebKit.App/Components/ApiClient/` subfolder created
- [ ] `_Imports.razor` updated with `@using SwebKit.App.Components.ApiClient` (BL-1 guard)
- [ ] `ApiClientPage.razor` — three-pane shell: collection tree | request builder | response viewer
- [ ] `CollectionTree.razor` — `<Virtualize>` over flattened node list, expand/collapse state
- [ ] Empty state component with actionable prompt

### Phase 2 — REST Execution
- [ ] `IHttpRequestExecutor` contract in `SwebKit.Core/Abstractions/`
- [ ] `HttpRequestExecutor` in `SwebKit.Core/Services/` using named `IHttpClientFactory`
- [ ] `IVariableSubstitutionService` + `VariableSubstitutionService` (regex `\{\{([^}]+)\}\}`)
- [ ] `RequestBuilderPanel.razor` — method selector, URL bar, tab strip: Params / Headers / Body / Auth
- [ ] Body editor sub-components: Monaco (JSON/XML/Text), key-value grid (Form Data), file picker (Binary)
- [ ] `ResponseViewerPanel.razor` — status badge, timing, size, Headers / Body / Raw tabs, Monaco read-only
- [ ] Cancellation via `CancellationTokenSource` on navigation away
- [ ] Named `HttpClient` registered in `MauiProgram.cs`

### Phase 3 — Environments and Secrets
- [ ] `EnvironmentManagerPanel.razor` — list environments, add/edit/delete
- [ ] `EnvironmentEditor.razor` — variable grid: key, type (Plain / SecretStore / KeyVault), value
- [ ] Secret type: value masked; stored in `ICredentialStore`
- [ ] Key Vault type: KV secret name input; resolved at execution time
- [ ] `IKeyVaultSecretResolver` contract in `SwebKit.Core/Abstractions/`
- [ ] `AzureKeyVaultSecretResolver` in `SwebKit.Azure/` using `Azure.Security.KeyVault.Secrets`
- [ ] KV setup prerequisite guard in Settings (new `KeyVaultUrl` in `AppConfig`)
- [ ] Active environment switcher in `ApiClientPage` toolbar
- [ ] Variable preview in request builder (resolved vs. masked display)

### Phase 4 — Authentication
- [ ] Auth tab wired in `RequestBuilderPanel.razor`
- [ ] `BearerAuthForm.razor` — token input (password-masked; `ICredentialStore` backed)
- [ ] `ApiKeyAuthForm.razor` — key name, value, placement radio (Header / Query Param)
- [ ] `BasicAuthForm.razor` — username + password (masked)
- [ ] `OAuth2AuthForm.razor` — flow selector, token URL, client ID, scopes, [Get Token] button
- [ ] Client credentials flow: token endpoint POST via `HttpClient`
- [ ] Auth code flow: `WebAuthenticator.AuthenticateAsync`
- [ ] `OAuth2TokenManager` — in-memory token cache + expiry refresh
- [ ] Auth never serialised to `collections.json` — only `CredentialKey` reference stored

### Phase 5 — GraphQL
- [ ] `GRAPHQL` pseudo-method in method selector
- [ ] Query editor pane (Monaco `graphql` mode)
- [ ] Variables editor pane (Monaco `json` mode)
- [ ] [Introspect Schema] button — `__schema` query + cache per endpoint
- [ ] Schema-aware autocomplete via `monaco-graphql` plugin
- [ ] GraphQL error rendering in `ResponseViewerPanel` (distinct from HTTP errors)

### Phase 6 — WebSocket
- [ ] `IWebSocketClientService` contract + `WebSocketClientService` (`ClientWebSocket` wrapper)
- [ ] Receive loop posts to `Channel<WebSocketMessage>`
- [ ] `IAsyncDisposable` cleanup on navigation away
- [ ] `WebSocketPanel.razor` — URL input, connect/disconnect, message composer, live message log
- [ ] Message log virtualized; messages timestamped with direction indicator (↑/↓)
- [ ] Binary frame support (hex display)

### Phase 7 — Export/Import
- [ ] `ICollectionExporter` / `ICollectionImporter` contracts in `SwebKit.Core/Abstractions/`
- [ ] `SwebKitCollectionExporter` + `SwebKitCollectionImporter` (versioned JSON)
- [ ] `PostmanCollectionExporter` — Postman Collection v2.1 output
- [ ] `PostmanCollectionImporter` — Postman Collection v2.1 subset parse
- [ ] `BrunoCollectionExporter` — `.bru` per-request files zipped
- [ ] `ConfigurationBundleService` extended to include `collections.json` + `environments.json`
- [ ] `ConfigurationBundleModels` extended with nullable `CollectionsData` and `EnvironmentsData`
- [ ] `CollectionExportDialog.razor` — format selector, include-environments checkbox, file save/open
- [ ] Format auto-detection on import (by file extension and magic bytes)

### Phase 8 — Performance and Polish
- [ ] Monaco lazy load — dynamic `import()` on first `/api-client` route activation
- [ ] Collection search/filter bar in `CollectionTree.razor`
- [ ] Request history sidebar — last N responses in-memory, per request
- [ ] Keyboard shortcuts registered in `CommandRegistry.cs` (Ctrl+Enter send, Ctrl+N new request,
      Ctrl+Shift+N new collection, Ctrl+E env manager)
- [ ] Drag-and-drop reordering within a collection (deferred; listed here as done-gate item)
- [ ] Response body truncation enforced at 500 KB + [Load full response] affordance
- [ ] Functional docs entry for `/api-client` in `docs/architecture/functionalities/`

## Completed Work

_Nothing yet._

## Blockers

_None._

## Validation Status

Not started.
