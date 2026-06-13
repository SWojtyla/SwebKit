# API Client

## What Is Supported

- **Collections and requests** — full tree of named folders and HTTP/GraphQL/WebSocket requests; persisted to `collections.json` via the atomic-write + `.bak` recovery pattern.
- **Variable substitution** — `{{token}}` syntax in URL, headers, body, and GraphQL variables; collection-level variables (always active) merged with the active environment; secrets resolved from Windows Credential Store or Azure Key Vault at send time.
- **Environments** — named environment sets with per-variable types: Plain, Windows Credential Store, Azure Key Vault. Active environment toggled from the toolbar. Full CRUD in the environment manager.
- **REST execution** — all methods (GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS); body modes: JSON, XML, Text, Form Data, Binary. 4 MB wire cap; response body displayed up to 500 KB with a [Load full response] affordance. Response: status badge, timing, size, headers, raw view.
- **Authentication** — Bearer Token, API Key (header or query param), Basic, OAuth 2.0 (Client Credentials + Auth Code with PKCE). Auth inherits from nearest folder or collection ancestor when not set on the request.
- **Post-request capture** — JSONPath, response header, or status code extraction; writes values into collection or environment variables.
- **GraphQL** — Monaco editor for query and variables; operation selector when multiple named operations are present; [Introspect Schema] button (cached per endpoint); subscription detection with `graphql-ws` framing; GraphQL Errors tab auto-selected on error responses.
- **WebSocket** — URL input with upgrade headers and optional subprotocol; connection state badge (Disconnected/Connecting/Connected/Faulted); virtualized message log capped at 10 000 frames (drop-oldest); Text/Binary composer; saved message templates per request.
- **Request history** — last 20 responses per request (in-memory, lost on restart); click a history entry to view; Re-send loads it back into the viewer.
- **Search/filter** — filter bar in the collection tree searches request and folder names across all collections.
- **Export/Import** — SwebKit JSON (lossless round-trip), Postman v2.1 (import + export), Bruno (export as zip of `.bru` files). Standalone environment file import. Name-collision auto-rename to "Name (2)".
- **Configuration bundle** — collections and environments are included in the SwebKit configuration bundle export/import.
- **Keyboard shortcuts** — `Ctrl+N` new request, `Ctrl+Shift+N` new collection, `Ctrl+E` env manager, `Ctrl+Enter` send, `Escape` cancel. All registered in `CommandRegistry` with `AreaScope = "api-client"`.

## Authentication

No external authentication is required. The API Client uses the user's own credentials (configured per-request or inherited) to send HTTP requests. Azure Key Vault resolution for environment variables uses `DefaultAzureCredential`.

## Core Runtime Flow

```
ApiClientPage
  ├── CollectionTree (Virtualize, flatten to List<FlatTreeNode>)
  │     └── filter bar — filters _visibleNodes on every keystroke
  ├── RequestBuilderPanel
  │     ├── URL bar + method selector
  │     ├── Tabs: Params | Headers | Body | Auth | Capture
  │     │     (GraphQL: GraphQL tab + Headers + Auth + Capture)
  │     │     (WebSocket: replaced entirely by WebSocketPanel)
  │     ├── GraphQlPanel (Monaco editors, introspection, operation selector)
  │     ├── WebSocketPanel (connect/disconnect, log, composer)
  │     └── AuthPanel → BearerAuthForm | ApiKeyAuthForm | BasicAuthForm | OAuth2AuthForm
  └── ResponseViewerPanel
        ├── History sidebar (last 20 per request)
        ├── Subscription message stream (GraphQL)
        ├── Status bar + tabs: Body | Headers | Raw | GraphQL Errors
        └── Body: 500 KB display cap + [Load full response] affordance
```

### Send path (REST)

1. `ApiClientPage.OnInitializedAsync` — loads `CollectionRepository` and `EnvironmentRepository` concurrently; kicks Monaco asset pre-load in the background.
2. User clicks Send (or presses `Ctrl+Enter`).
3. `RequestBuilderPanel.OnSendAsync` detects WebSocket/subscription/HTTP and routes accordingly.
4. `HttpRequestExecutor.ExecuteAsync`:
   - Calls `IVariableSubstitutionService.BuildScopeAsync` (resolves KV secrets).
   - Builds URL with substituted query params.
   - Builds `HttpRequestMessage` with substituted headers and body.
   - Calls `IAuthHeaderBuilder.ApplyAsync` (applies resolved auth headers).
   - Sends via named `HttpClient("ApiClient")`.
   - Parses GraphQL errors when `Method == GraphQl`.
   - Calls `IPostRequestCaptureExecutor.ExecuteAsync` (JSONPath/header/status extraction).
5. Result flows back to `ApiClientPage.OnRequestResultAsync` → recorded in history → passed to `ResponseViewerPanel`.

### WebSocket path

1. `WebSocketPanel.ConnectAsync` — creates `IWebSocketClientService`, calls `ConnectAsync`, starts background `RunReadLoopAsync` (BL-7).
2. Incoming frames arrive on a bounded `Channel<WebSocketMessage>` (10 000 cap, `DropOldest`).
3. `ReadAsync` on the channel delivers frames to the UI loop via `InvokeAsync(StateHasChanged)`.

### GraphQL subscription path

1. `RequestBuilderPanel.IsSubscriptionOperation()` — regex detects `subscription` keyword.
2. `GraphQlSubscriptionService.RunAsync` — `graphql-ws` handshake over `WebSocketClientService`, streams `next` frames to `OnSubscriptionMessage` callback.
3. Messages accumulate in `ApiClientPage._subscriptionMessages` (1 000 cap, drop-oldest) and are passed to `ResponseViewerPanel.SubscriptionMessages`.

## State Persistence

| State                  | Location                               | Lifetime        |
| ---------------------- | -------------------------------------- | --------------- |
| Collections + requests | `AppData/collections.json`             | Persistent      |
| Environments           | `AppData/environments.json`            | Persistent      |
| Active environment     | `AppData/environments.json` (UiState)  | Persistent      |
| Last selected request  | `AppData/environments.json` (UiState)  | Persistent      |
| Request history        | `ApiClientPage._requestHistory` (dict) | Session only    |
| WS message log         | `WebSocketPanel._messages` (list)      | Session only    |
| GraphQL subscription   | `ApiClientPage._subscriptionMessages`  | Session only    |
| OAuth2 token cache     | `OAuth2TokenManager._cache` (dict)     | Session only    |
| Monaco assets          | Browser WebView cache                  | Browser session |
