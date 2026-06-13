# Status — API Client

## Current State

`In Progress`

## Current Focus

Phase 6 (WebSocket) complete — 598 tests passing. Ready for Phase 7 (Export/Import).

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

### Phase 4 — Authentication ✅

- [x] `Collection.DefaultAuth: AuthConfig?` and `RequestFolder.DefaultAuth: AuthConfig?` — already in domain model
- [x] `AuthConfig` extended: `BasicUsername`, `OAuth2ClientId`, `OAuth2AuthUrl` added
- [x] `IAuthInheritanceResolver` contract in `SwebKit.Core/Abstractions/`
- [x] `AuthInheritanceResolver` — walks request → nearest folder → collection; registered as `Singleton`
- [x] `IAuthHeaderBuilder` contract in `SwebKit.Core/Abstractions/`
- [x] `AuthHeaderBuilder` in `SwebKit.App/Services/` — applies Bearer/ApiKey/Basic/OAuth2 headers
- [x] `IOAuth2TokenManager` contract in `SwebKit.Core/Abstractions/`
- [x] `OAuth2TokenManager` in `SwebKit.App/Services/` — client credentials + auth code (PKCE); in-memory cache with 60 s early-refresh window; refresh token persisted to `ICredentialStore`
- [x] `HttpRequestExecutor` updated — injects `IAuthInheritanceResolver` + `IAuthHeaderBuilder`; applies auth before each request
- [x] `BearerAuthForm.razor` — token input (password-masked; `ICredentialStore` backed)
- [x] `ApiKeyAuthForm.razor` — key name, value, placement radio (Header / Query Param)
- [x] `BasicAuthForm.razor` — username + password (masked)
- [x] `OAuth2AuthForm.razor` — grant type selector, token/auth URL, client ID, scopes, [Get Token] / [Authorize…] button
- [x] `AuthPanel.razor` — type selector + inherited-from badge + sub-form dispatch
- [x] `RequestBuilderPanel.razor` — Auth stub replaced with `<AuthPanel>`
- [x] `MauiProgram.cs` — registers `IOAuth2TokenManager`, `IAuthHeaderBuilder`, `IAuthInheritanceResolver`
- [x] `decisions.md` — PENDING-1 resolved as DEC-17 (redirect URI: `sweb://oauth` via MAUI WebAuthenticator)
- [x] Build clean: 0 errors
- [x] Unit tests — `AuthInheritanceResolverTests` (11 tests) — 11 new tests
- [x] Total: 556 tests passing

### Phase 5 — GraphQL ✅

- [x] `GRAPHQL` pseudo-method in method selector (was already in `ApiRequestMethod.GraphQl` enum)
- [x] `GraphQlQuery`, `GraphQlVariables`, `GraphQlSelectedOperation` fields added to `HttpRequestEntry`
- [x] `GraphQlError`, `GraphQlErrorLocation`, `GraphQlSubscriptionMessage` types added to `ApiClientModels.cs`
- [x] `GraphQlErrors` property added to `HttpRequestResult`
- [x] `IGraphQlSchemaService` contract in `SwebKit.Core/Abstractions/` — introspection + operation parsing + cache
- [x] `GraphQlSchemaService` in `SwebKit.Core/Services/` — sends `__schema` introspection query; in-memory cache per URL; `ParseOperationNames` via regex
- [x] `IGraphQlSubscriptionService` contract in `SwebKit.Core/Abstractions/`
- [x] `GraphQlSubscriptionService` in `SwebKit.Core/Services/` — `graphql-ws` framing (`connection_init` / `subscribe` / `next` / `complete` / `ping-pong`)
- [x] `IWebSocketClientService` contract in `SwebKit.Core/Abstractions/` (Phase 6 will expand)
- [x] `BasicWebSocketClientService` in `SwebKit.Core/Services/` — `ClientWebSocket` backed minimal implementation
- [x] `HttpRequestExecutor` updated: builds GraphQL JSON body from `GraphQlQuery`/`GraphQlVariables`/`operationName`; parses `errors` array from response into `GraphQlErrors`
- [x] `GraphQlPanel.razor` — Monaco editors for query (`graphql` language) and variables (`json` language); collapsible variables section; operation selector dropdown (>1 named operation); [Introspect Schema] button with loading state; dismissible introspection error banner (BL-6 lazy Monaco load)
- [x] `RequestBuilderPanel.razor` updated: GraphQL tab strip `["GraphQL", "Headers", "Auth", "Capture"]` when method=GraphQl; tab switch guards; subscription detection via `subscription` keyword; [Stop] button visible during active subscription
- [x] `ResponseViewerPanel.razor` updated: GraphQL Errors tab (auto-selected when errors present); subscription message stream with `<Virtualize>` (timestamped, pretty-printed JSON, error badges)
- [x] `ApiClientPage.razor` updated: subscription message accumulation; callbacks to `RequestBuilderPanel`; passes `SubscriptionMessages` to `ResponseViewerPanel`
- [x] Services registered in `MauiProgram.cs`: `IGraphQlSchemaService` (Singleton), `IGraphQlSubscriptionService` (Transient)
- [x] Unit tests — `GraphQlSchemaServiceTests` (9 tests) + `GraphQlErrorParsingTests` (7 tests) — 16 new tests
- [x] Total: 572 tests passing, build clean (pre-existing MSIX signing error only)

### Phase 6 — WebSocket ✅

- [x] `WebSocketMessage`, `WebSocketSavedMessage`, `WebSocketConnectionState`, `WebSocketMessageDirection`, `WebSocketFrameType` types added to `ApiClientModels.cs`
- [x] `HttpRequestEntry` extended: `SavedMessages: List<WebSocketSavedMessage>`, `WsSubProtocol: string?`
- [x] `IWebSocketClientService` upgraded — `State`, `SendBinaryAsync`, `ReadAsync`, `FrameCap = 10 000` cap; removed `ReceiveTextAsync`
- [x] `WebSocketClientService` in `SwebKit.Core/Services/` — `Channel<WebSocketMessage>` bounded with `BoundedChannelFullMode.DropOldest`; background receive loop; binary frame hex display; `IAsyncDisposable`
- [x] `BasicWebSocketClientService` removed (superseded by `WebSocketClientService`)
- [x] `GraphQlSubscriptionService` updated to use `WebSocketClientService` and `ReadAsync`
- [x] `WebSocketPanel.razor` — URL input; subprotocol field; upgrades headers tab; connection state badge (Disconnected/Connecting/Connected/Faulted); virtualized message log with ↑/↓ direction, timestamp, size; [Clear log] button; composer with Text/Binary selector; saved message template dropdown; [Save…] button with dialog; [Connect]/[Disconnect] buttons; full `IAsyncDisposable` cleanup (BL-7)
- [x] `RequestBuilderPanel.razor` updated: WebSocket method hides the standard URL/Send bar and tab strip; renders `WebSocketPanel` as full content area; WS method picker still accessible
- [x] `IWebSocketClientService` registered as `Transient` in `MauiProgram.cs`
- [x] `uiState.js` extended: `SwebKitUi.scrollToBottom` for auto-scroll on send
- [x] Unit tests — `WebSocketClientServiceTests` (2 tests) + `WebSocketDomainModelTests` (8 tests) + `WebSocketChannelOverflowTests` (1 test) — 12 new tests (verified drop-oldest channel behaviour)
- [x] Total: 598 tests passing, build clean (pre-existing MSIX signing error only)

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
