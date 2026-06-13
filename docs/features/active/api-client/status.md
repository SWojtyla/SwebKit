# Status — API Client

## Current State

`In Progress`

## Current Focus

Phase 9 complete — linked-root loading, sparse request files, linked environments, conflict-safe save-back, Linked Root Manager, secret configuration, scoped Git actions, and compare helpers are implemented.

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

### Phase 7 — Export/Import ✅

- [x] `ICollectionExporter`, `ICollectionImporter`, `IEnvironmentImporter` contracts in `SwebKit.Core/Abstractions/ICollectionExportImport.cs`; `CollectionImportResult`, `EnvironmentImportResult` result types
- [x] `SwebKitCollectionExporter` — lossless round-trip to versioned JSON (`schemaVersion`, `collection`, `environments`); `.sweb.json` extension
- [x] `SwebKitCollectionImporter` — detects by presence of `schemaVersion` + `collection` keys; reports request/capture/auth counts
- [x] `SwebKitEnvironmentImporter` — imports standalone `EnvironmentsStore` JSON
- [x] `PostmanCollectionExporter` — Postman Collection v2.1 output; headers, query params, body (raw/formdata/graphql), auth
- [x] `PostmanCollectionImporter` — v2/v2.1 subset parse; collection variables extracted as new `ApiEnvironment` named `"<Name> (imported)"`; nested folder support; auth flags
- [x] `BrunoCollectionExporter` — one `.bru` file per request zipped; folder hierarchy as subdirectories; environment vars file; secrets marked `vars:secret`
- [x] `CollectionImportService` — auto-detects format; name collision resolution (`Name (2)`, `Name (3)`, etc.); persists via repositories
- [x] `CollectionRepository.AddImportedCollectionAsync` + `EnvironmentRepository.AddImportedEnvironmentAsync` (new-ID variants)
- [x] `ConfigurationBundleModels` extended: nullable `CollectionsData: CollectionsStore?`, `EnvironmentsData: EnvironmentsStore?` (backward-compatible)
- [x] `ConfigurationBundleService` extended: constructor accepts `CollectionRepository` + `EnvironmentRepository`; `Export()` includes API client data; `ImportAsync()` restores it when present
- [x] `CollectionExportDialog.razor` — Export tab (format selector, include-environments checkbox); Import tab (collection file picker, standalone environment file picker, import summary panel, auth re-entry warning)
- [x] `ApiClientPage.razor` — [Export / Import] toolbar button added; `OnCollectionImportedAsync` reloads repositories on import
- [x] `uiState.js` extended: `SwebKitUi.downloadBinaryFile` for binary/ZIP downloads
- [x] All new services registered in `MauiProgram.cs`
- [x] Unit tests — `SwebKitExportImportTests` (5) + `PostmanExportImportTests` (5) + `CollectionImportServiceTests` (4) + `SwebKitEnvironmentImporterTests` (2) + `BrunoCollectionExporterTests` (2) — 18 new tests
- [x] Total: 623 tests passing, build clean (pre-existing MSIX signing error only)

### Phase 8 — Performance and Polish ✅

- [x] **Monaco lazy load** — `JS.InvokeVoidAsync("SwebKitUi.ensureMonacoLoaded")` fired on `ApiClientPage.OnInitializedAsync` so Monaco assets are pre-warmed before the user opens a GraphQL request
- [x] **Collection search/filter bar** — already implemented in `CollectionTree.razor` (`_filter` field, `OnFilterInput`, `ApplyFilter`); searches all collections by name across the full flat tree
- [x] **Request history sidebar** — last 20 responses per request kept in `ApiClientPage._requestHistory` (`Dictionary<string, List<HttpRequestResult>>`); `ResponseViewerPanel` shows a collapsible history sidebar with status badge, method, and elapsed time; clicking an entry loads it into the viewer; [Re-send] is fire-and-forget (no re-execution)
- [x] **Keyboard shortcuts registered in `CommandRegistry.cs`** — `Ctrl+N` new request, `Ctrl+Shift+N` new collection, `Ctrl+E` env manager; `ApiClientShortcutEvent` published by `MainLayout.OnShortcut`; `ApiClientPage` subscribes/unsubscribes on lifecycle; JS shortcuts added to `keyboardShortcuts.js` under the `!inInput` guard
- [x] **Response body truncation at 500 KB** — `ResponseViewerPanel.GetDisplayBody()` clips to 500 KB display limit; `IsBodyDisplayTruncated` drives a `[Load full response (X MB)]` affordance below the body; `_showFullBody` flag lifted on click; pretty-print still applied on truncated JSON
- [x] **Functional docs** — `docs/architecture/functionalities/api-client.md` created with full feature list, auth notes, runtime flow diagram, send/WS/subscription paths, and state persistence table
- [x] Drag-and-drop reordering: **explicitly deferred** (post-Phase-8)
- [x] Total: 623 tests passing, build clean (pre-existing MSIX signing error only)

### Phase 9 — Git-Linked Collections In Progress

- [x] Define SwebKit-native folder format for linked API roots (`.swebkit-api/swebkit.json`)
- [x] Implement compact request files with omitted defaults and optional body/query sidecars
- [x] Add linked-root configuration persistence in app-local settings (`api-linked-roots.json`)
- [x] Load multiple linked roots beside local collections in the collection tree
- [x] Add Add Linked Root dialog with create/use-existing root behavior
- [x] Add Git status provider (branch, clean/dirty, changed API files)
- [x] Add conflict detection before overwriting linked request files changed on disk
- [x] Add Linked Root Manager panel
- [x] Add missing-secret detection/hint for linked requests that reference `{{secret:name}}`
- [x] Add safe Git actions: create branch, switch via branch dropdown, stage/unstage/revert API files, commit staged API files, push current branch
- [x] Load linked environment files and merge them into the environment picker
- [x] Add configure-secret flow for linked environments; secret values are stored locally in `ICredentialStore`
- [x] Add remote compare/open helper for GitHub and Azure DevOps remotes
- [x] Add tests for linked-root creation, linked collection creation, sparse request defaults, sidecar load/save, environment load/save, conflict detection, non-repo Git status, branch listing/validation, scoped API-file stage/unstage/revert, scoped staged commits, and compare URL inference

## Completed Work

All 8 original phases complete. Phase 9 linked-root implementation now covers format/load/save, linked environments, tree UI, root manager, linked-root selection, conflict detection, Git status/actions, branch dropdown switching, staged file actions, compare helpers, and secret configuration.

## Blockers

_None._

## Validation Status

Phase 9 focused linked-root unit tests passing; MAUI app build passing.

## Planned Follow-Up

### Phase 10 — Dynamic Variables Complete

- [x] Add generated variable definitions for collection/environment variables
- [x] Add primitive generators: integer/decimal range, boolean, GUID, date/time, list pick, template
- [x] Add `Bogus`-backed fake data generators for first name, last name, email, phone, and company
- [x] Extend variable substitution and preview to resolve generated values per send/preview
- [x] Add generated-variable editors in environment and collection variable screens
- [x] Extend linked collection/environment file format with `generatedVariables`
- [x] Add focused tests for constraints, faker values, template composition, scope resolution, and serialization
