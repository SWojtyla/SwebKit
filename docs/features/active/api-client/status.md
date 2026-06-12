# Status — API Client

## Current State

`Planned`

## Current Focus

Phase 1 — Foundation (not yet started)

## Progress Checklist

### Phase 1 — Foundation

- [ ] `ApiClientModels.cs` — `Collection` (+ `List<CollectionVariable>`, `AuthConfig? DefaultAuth`),
      `RequestFolder` (+ `AuthConfig? DefaultAuth`), `HttpRequestEntry` (+ `List<CaptureRule>`),
      `ApiEnvironment`, `EnvironmentVariable`, `AuthConfig`, `CollectionVariable`, `CaptureRule`
      in `SwebKit.Core/Domain/`
- [ ] `CollectionRepository` — atomic write + `.bak` recovery, `collections.json` via `AppDataFileStore`
- [ ] `EnvironmentRepository` — atomic write + `.bak` recovery, `environments.json`
- [ ] `UserSettingsRepository` extended: `AutoSaveRequests: bool` (default: `false`)
- [ ] Both collection/environment repositories registered in `MauiProgram.cs`
- [ ] Route `/api-client` wired in `Routes.razor`
- [ ] Sidebar entry in `LeftNav.razor`
- [ ] `src/SwebKit.App/Components/ApiClient/` subfolder created
- [ ] `_Imports.razor` updated with `@using SwebKit.App.Components.ApiClient` (BL-1 guard)
- [ ] `ApiClientPage.razor` — two-panel layout: collection tree left | request builder+response right
      (single-request focus model — one request open at a time)
- [ ] `CollectionTree.razor` — `<Virtualize>` over flattened node list, expand/collapse state
- [ ] `RequestQuickNavPanel.razor` — collapsible left-sidebar list of all requests across
      collections; `Ctrl+P` to focus; click a row to open that request in the builder
- [ ] [+ New Request] button in toolbar; keyboard shortcut `Ctrl+N`; creates default empty GET
      request named "New Request" in the active/selected collection
- [ ] [+ New Collection] button in toolbar; keyboard shortcut `Ctrl+Shift+N`
- [ ] Auto-save: when `AutoSaveRequests` is `true`, debounce 500 ms after last edit then call
      `CollectionRepository.SaveAsync`; dirty indicator (asterisk in panel header) when unsaved
- [ ] Empty state component with actionable prompt ("Create a collection to get started")

### Phase 2 — REST Execution

- [ ] `IHttpRequestExecutor` contract in `SwebKit.Core/Abstractions/`
- [ ] `HttpRequestExecutor` in `SwebKit.Core/Services/` using named `IHttpClientFactory`
- [ ] `HttpRequestResult` extended with `ResponseBodyTruncated: bool`
- [ ] `IVariableSubstitutionService` + `VariableSubstitutionService`
      (resolution order: collection vars → env vars → `ICredentialStore` → KV)
- [ ] `IVariablePreviewService` + `VariablePreviewService` — returns
      `Dictionary<string, string?>` of token→resolved-value for display only (no substitution
      side-effects; secrets masked as `••••••••`)
- [ ] `RequestBuilderPanel.razor` — method selector, URL bar, tab strip: Params / Headers / Body / Auth
- [ ] URL bar variable preview: `{{variable}}` tokens rendered with a small resolved-value badge
      below them (populated by `IVariablePreviewService` on URL change, debounced 300 ms)
- [ ] Body editor variable preview: same preview service called when body content changes and
      `{{` tokens are detected; shown in a preview strip above the Monaco editor
- [ ] Body editor sub-components: Monaco (JSON/XML/Text), key-value grid (Form Data), file picker (Binary)
- [ ] `ResponseViewerPanel.razor` — status badge, timing, size, Headers / Body / Raw tabs, Monaco read-only
- [ ] Cancellation via `CancellationTokenSource` on navigation away
- [ ] Named `HttpClient` registered in `MauiProgram.cs` (follow redirects on by default; 30 s timeout)
- [ ] SSL verification: global setting in `UserSettings`; `HttpClientHandler.ServerCertificateCustomValidationCallback`
      bypass when disabled (dev-only; surfaced with a visible warning badge in the toolbar)

### Phase 3 — Environments and Secrets

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
