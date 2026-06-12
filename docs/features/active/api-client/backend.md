# Backend — API Client

## Domain Model

**New file:** `src/SwebKit.Core/Domain/ApiClientModels.cs`

Follows the same record-based immutable design used throughout `SwebKit.Core/Domain/`.

```csharp
// ── Persisted root ──────────────────────────────────────────────────────────

public record ApiClientData
{
    public List<Collection> Collections { get; init; } = [];
    public string? ActiveEnvironmentId { get; init; }
    public string SchemaVersion { get; init; } = "1";
}

// ── Collections and nodes ────────────────────────────────────────────────────

public record Collection(string Id, string Name)
{
    public string? Description { get; init; }
    public List<CollectionNode> Nodes { get; init; } = [];
    public List<CollectionVariable> CollectionVariables { get; init; } = [];
    public AuthConfig? DefaultAuth { get; init; }  // inherited by all requests in this collection
}

// Always-active variables scoped to one collection; no environment needed.
// Resolution: collection vars first, then env vars (env takes precedence on same key).
public record CollectionVariable(string Key, CollectionVariableType Type)
{
    public string? PlainValue { get; init; }
    public string? CredentialStoreKey { get; init; }  // SecretStore type
}

public enum CollectionVariableType { Plain, SecretStore }

// Polymorphic node — folder or request leaf
[JsonPolymorphic(TypeDiscriminatorPropertyName = "nodeType")]
[JsonDerivedType(typeof(RequestFolder), "folder")]
[JsonDerivedType(typeof(HttpRequestEntry), "request")]
[JsonDerivedType(typeof(WebSocketEntry), "websocket")]
public abstract record CollectionNode(string Id, string Name);

public record RequestFolder(string Id, string Name) : CollectionNode(Id, Name)
{
    public List<CollectionNode> Children { get; init; } = [];
    public AuthConfig? DefaultAuth { get; init; }  // inherited by requests in this folder
}

public record HttpRequestEntry(string Id, string Name, string Method, string Url)
    : CollectionNode(Id, Name)
{
    public List<HeaderEntry> Headers { get; init; } = [];
    public List<QueryParam> QueryParams { get; init; } = [];
    public RequestBody? Body { get; init; }
    // null means "inherit auth from parent folder or collection" (see IAuthInheritanceResolver)
    public AuthConfig? Auth { get; init; }
    // GraphQL — null for non-GraphQL requests
    public string? GraphQlVariables { get; init; }
    // Post-request capture rules — applied after response received
    public List<CaptureRule> CaptureRules { get; init; } = [];
}

public record WebSocketEntry(string Id, string Name, string Url)
    : CollectionNode(Id, Name)
{
    public List<HeaderEntry> Headers { get; init; } = [];  // includes Sec-WebSocket-Protocol
    public List<SavedMessage> SavedMessages { get; init; } = [];
}

public record SavedMessage(string Name, string Content);

public record HeaderEntry(string Key, string Value, bool Enabled = true);
public record QueryParam(string Key, string Value, bool Enabled = true);

// ── Request body ─────────────────────────────────────────────────────────────

public record RequestBody(RequestBodyType Type)
{
    public string? Content { get; init; }                  // raw JSON/XML/Text
    public List<FormField>? FormFields { get; init; }      // FormData
    // Binary path stored as a credential-store reference or temp path — never persisted inline
}

public enum RequestBodyType { None, Json, Xml, Text, FormData, Binary }

public record FormField(string Key, string Value, bool Enabled = true);

// ── Post-request capture rules ────────────────────────────────────────────────
// Rules applied after response received; populate collection or environment variables
// automatically using building blocks — no code writing required.

public record CaptureRule(CaptureSourceType SourceType, string SourceExpression,
    string TargetVariableName, CaptureTargetScope TargetScope);

public enum CaptureSourceType
{
    JsonPath,       // SourceExpression is a JSONPath expression evaluated on response body
    Header,         // SourceExpression is the response header name
    StatusCode      // SourceExpression ignored; captures HTTP status code as string
}

public enum CaptureTargetScope { Collection, Environment }

// ── Authentication ────────────────────────────────────────────────────────────

// SECURITY: AuthConfig stores only a CredentialKey reference into ICredentialStore.
// Actual tokens, passwords, and client secrets are NEVER written to collections.json.
public record AuthConfig(AuthType Type)
{
    public string? CredentialKey { get; init; }       // bearer, basic, OAuth2 token/secret
    public string? ApiKeyName { get; init; }
    public ApiKeyPlacement ApiKeyPlacement { get; init; }
    public OAuth2Config? OAuth2 { get; init; }
}

public enum AuthType { None, BearerToken, ApiKey, Basic, OAuth2ClientCredentials, OAuth2AuthCode }
public enum ApiKeyPlacement { Header, QueryParam }

public record OAuth2Config(string TokenUrl, string ClientId, string Scopes)
{
    // ClientSecret itself stored in ICredentialStore via AuthConfig.CredentialKey
    public string? AuthorizationUrl { get; init; }    // auth code flow only
    public string? RedirectUri { get; init; }
}
```

**New file:** `src/SwebKit.Core/Domain/ApiClientEnvironmentModels.cs`

```csharp
public record ApiEnvironmentData
{
    public List<ApiEnvironment> Environments { get; init; } = [];
    public string SchemaVersion { get; init; } = "1";
}

public record ApiEnvironment(string Id, string Name)
{
    public List<EnvironmentVariable> Variables { get; init; } = [];
}

public record EnvironmentVariable(string Key, EnvironmentVariableType Type)
{
    public string? PlainValue { get; init; }
    public string? CredentialStoreKey { get; init; }    // SecretStore type
    public string? KeyVaultSecretName { get; init; }    // KeyVault type
}

public enum EnvironmentVariableType { Plain, SecretStore, KeyVault }
```

---

## Repositories (SwebKit.Core/Configuration/)

### `CollectionRepository.cs`

Follows the exact same pattern as `ProfileRepository` and `UiStateRepository`:

- `LoadAsync()` → tries `collections.json`, falls back to `collections.json.bak`, returns
  default `ApiClientData` if both missing
- `SaveAsync(ApiClientData data)` → atomic temp-file replace → refresh `.bak`
- Uses `AppDataFileStore` for path resolution
- `JsonSerializerContext` extended with `ApiClientData`

### `EnvironmentRepository.cs`

Same pattern; persists `environments.json` / `environments.json.bak`.

Both repositories registered as **singletons** in `MauiProgram.cs` (same as `ProfileRepository`).

---

## HTTP Request Executor

**Contract:** `src/SwebKit.Core/Abstractions/IHttpRequestExecutor.cs`

```csharp
public interface IHttpRequestExecutor
{
    Task<HttpRequestResult> ExecuteAsync(
        HttpRequestEntry request,
        ApiEnvironment? environment,
        CancellationToken cancellationToken = default);
}

public record HttpRequestResult(
    int StatusCode,
    string StatusText,
    List<HeaderEntry> ResponseHeaders,
    string Body,
    TimeSpan Elapsed,
    long ResponseSizeBytes,
    bool IsSuccess);
```

**Implementation:** `src/SwebKit.Core/Services/HttpRequestExecutor.cs`

- Receives `IHttpClientFactory` (named client `"ApiClient"` registered in `MauiProgram.cs`)
- Calls `IVariableSubstitutionService.SubstituteAsync` on URL, header values, and body before send
- Injects auth via `IAuthHeaderBuilder` helper (per `AuthType`)
- Caps response body read at 500 KB; sets `ResponseSizeBytes` to actual content-length when known
- Returns `HttpRequestResult` with `IsSuccess = statusCode < 400`
- `GraphQL` pseudo-method → serialises as `POST` with `Content-Type: application/json` and
  body `{ "query": "...", "variables": {...} }`

---

## Variable Substitution Service

**Contract:** `src/SwebKit.Core/Abstractions/IVariableSubstitutionService.cs`

```csharp
public interface IVariableSubstitutionService
{
    Task<string> SubstituteAsync(
        string input,
        ApiEnvironment? environment,
        CancellationToken ct = default);
}
```

**Implementation:** `src/SwebKit.Core/Services/VariableSubstitutionService.cs`

- Regex: `\{\{([^}]+)\}\}` — matches all `{{key}}` tokens
- **Resolution order per token** (first match wins):
  1. Environment variable (plain value) from active `ApiEnvironment.Variables`
  2. Environment secret via `ICredentialStore` (masked in preview, substituted for execution)
  3. Environment KV type via `IKeyVaultSecretResolver.ResolveAsync`
  4. Collection variable (plain value) from `Collection.CollectionVariables`
  5. Collection secret via `ICredentialStore`
     > Environment variables override collection variables on the same key.
- When KV resolver returns `null` → substitutes `[KV_UNAVAILABLE:key]`
- When no environment active → only collection variables are resolved; unmatched tokens preserved
- Registered as `Scoped`

---

## Variable Preview Service

**Contract:** `src/SwebKit.Core/Abstractions/IVariablePreviewService.cs`

```csharp
public interface IVariablePreviewService
{
    // Returns all {{key}} tokens found in input, mapped to their preview value.
    // Secrets are returned as "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022" (masked).
    // KV variables show the secret name (not the secret value) for safety.
    Task<IReadOnlyDictionary<string, string>> PreviewAsync(
        string input,
        Collection collection,
        ApiEnvironment? environment,
        CancellationToken ct = default);
}
```

**Implementation:** `src/SwebKit.Core/Services/VariablePreviewService.cs`

- Same regex as substitution service; does NOT call `IKeyVaultSecretResolver` (preview only)
- Secrets: returns `"\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022"` regardless of actual value
- Unresolved tokens returned as `null` value (UI renders as greyed-out)
- Registered as `Scoped`

---

## Post-Request Capture Executor

**Contract:** `src/SwebKit.Core/Abstractions/IPostRequestCaptureExecutor.cs`

```csharp
public interface IPostRequestCaptureExecutor
{
    Task<CaptureExecutionResult> ExecuteAsync(
        IReadOnlyList<CaptureRule> rules,
        HttpRequestResult response,
        Collection collection,
        ApiEnvironment? environment,
        CancellationToken ct = default);
}

public record CaptureExecutionResult(
    int SuccessCount,
    IReadOnlyList<CaptureWarning> Warnings);

public record CaptureWarning(string RuleSummary, string Reason);
```

**Implementation:** `src/SwebKit.Core/Services/PostRequestCaptureExecutor.cs`

- For each `CaptureRule`:
  - `JsonPath`: parse `response.Body` as `JsonDocument`, evaluate expression using `JsonPath.Net`;
    extracted value cast to string; on any exception → adds a `CaptureWarning`, continues
  - `Header`: look up `response.ResponseHeaders` by key (case-insensitive); missing → `CaptureWarning`
  - `StatusCode`: captures `response.StatusCode.ToString()`
- Successful extraction:
  - `TargetScope.Collection` → calls `CollectionRepository.UpdateCollectionVariableAsync`
  - `TargetScope.Environment` → calls `EnvironmentRepository.UpdateVariableAsync`
- Never throws; all errors become `CaptureWarning` entries
- `HttpRequestExecutor.ExecuteAsync` calls this after receiving the response
- Registered as `Scoped`

**New NuGet:** `JsonPath.Net` (`json-everything` suite) added to `SwebKit.Core`

---

## Auth Inheritance Resolver

**Contract:** `src/SwebKit.Core/Abstractions/IAuthInheritanceResolver.cs`

```csharp
public interface IAuthInheritanceResolver
{
    // Returns the effective AuthConfig for a request, walking up the tree.
    // Returns null only when no auth is configured anywhere in the hierarchy.
    AuthConfig? Resolve(
        HttpRequestEntry request,
        RequestFolder? parentFolder,
        Collection collection);
}
```

**Implementation:** `src/SwebKit.Core/Services/AuthInheritanceResolver.cs`

- Returns `request.Auth` if non-null
- Else walks parent `RequestFolder.DefaultAuth` (caller provides direct parent)
- Else returns `collection.DefaultAuth`
- `HttpRequestExecutor` resolves auth before injecting headers
- Registered as `Scoped`

---

## GraphQL Subscription Service

**Contract:** `src/SwebKit.Core/Abstractions/IGraphQlSubscriptionService.cs`

```csharp
public interface IGraphQlSubscriptionService : IAsyncDisposable
{
    GraphQlSubscriptionState State { get; }
    IAsyncEnumerable<string> SubscribeAsync(
        string url,
        string query,
        string? variables,
        string? operationName,
        IReadOnlyList<HeaderEntry> headers,
        CancellationToken ct = default);
    Task StopAsync();
}

public enum GraphQlSubscriptionState { Disconnected, Connecting, Subscribed, Faulted }
```

**Implementation:** `src/SwebKit.Core/Services/GraphQlSubscriptionService.cs`

- Uses `IWebSocketClientService` internally (transient; injected via factory `Func<IWebSocketClientService>`)
- `graphql-ws` protocol framing:
  1. `ConnectAsync` with `Sec-WebSocket-Protocol: graphql-transport-ws`
  2. Send `{"type":"connection_init"}`
  3. Await `connection_ack`
  4. Send `{"type":"subscribe","id":"1","payload":{"query":"...","variables":{...}}}`
  5. Yield each `next` message's `data` payload as a JSON string
  6. Stop on `complete`, `error`, or cancellation
- `StopAsync` sends `{"type":"complete","id":"1"}` and disconnects
- Registered as `Transient`

---

## Postman Import — Environment Extraction

`PostmanCollectionImporter` extended:

- Reads `collection.variable[]` array (Postman collection-level variables)
- Creates a new `ApiEnvironment` named `"<CollectionName> (imported)"` containing those variables
  as plain-value environment variables
- Returns both the `Collection` and the extracted `ApiEnvironment` in `ImportResult`
- `ImportResult` extended: `ApiEnvironment? ExtractedEnvironment`
- If `ExtractedEnvironment` is non-null, the import dialog offers to activate it automatically

---

## Standalone Environment Import

**New contract:** `src/SwebKit.Core/Abstractions/IEnvironmentImporter.cs`

```csharp
public interface IEnvironmentImporter
{
    Task<EnvironmentImportResult> ImportAsync(Stream source, CancellationToken ct = default);
    bool CanImport(string fileExtension, byte[] header);
}

public record EnvironmentImportResult(
    bool Success,
    ApiEnvironment? Environment,
    string? ErrorMessage);
```

**Implementation:** `SwebKitEnvironmentImporter` — reads SwebKit-native environment JSON (same schema as
`environments.json` `ApiEnvironmentData`); name collision on import renames to "Name (2)"

**Contract:** `src/SwebKit.Core/Abstractions/IKeyVaultSecretResolver.cs`

```csharp
public interface IKeyVaultSecretResolver
{
    Task<string?> ResolveAsync(string secretName, CancellationToken ct = default);
}
```

**Implementation:** `src/SwebKit.Azure/KeyVaultSecretResolver.cs`

- Uses `Azure.Security.KeyVault.Secrets.SecretClient` with `DefaultAzureCredential`
- KV URL sourced from `AppConfig.KeyVault.Url` (new property under `AppConfig`)
- Returns `null` (never throws) on `RequestFailedException` or `CredentialUnavailableException`
- Registered as `Scoped`; `NoopKeyVaultSecretResolver` stub registered when KV URL is absent

**`AppConfig` addition:** `src/SwebKit.Core/Domain/AppConfig.cs`

```csharp
public class AppConfig
{
    // ... existing fields ...
    public KeyVaultConfig? KeyVault { get; set; }
}

public class KeyVaultConfig
{
    public string? Url { get; set; }
}
```

---

## OAuth 2 Token Manager

**New file:** `src/SwebKit.Core/Services/OAuth2TokenManager.cs`

- **Client credentials flow:** POST to `OAuth2Config.TokenUrl` with `client_id`, `client_secret`
  (resolved from `ICredentialStore`), `scope`, `grant_type=client_credentials`
- **Auth code flow:** calls `Microsoft.Maui.Authentication.WebAuthenticator.AuthenticateAsync`
  with PKCE; exchanges code for token
- In-memory token cache keyed by `AuthConfig.CredentialKey`; checks `expires_in` and re-fetches
  within 60 seconds of expiry
- Never stores the fetched token to disk — only lives in the cache for the session

---

## WebSocket Client Service

**Contract:** `src/SwebKit.Core/Abstractions/IWebSocketClientService.cs`

```csharp
public interface IWebSocketClientService : IAsyncDisposable
{
    WebSocketConnectionState State { get; }
    IAsyncEnumerable<WebSocketMessage> ReceiveAsync(CancellationToken ct = default);
    // subprotocols: passed as Sec-WebSocket-Protocol header values
    Task ConnectAsync(string url, IReadOnlyList<HeaderEntry> headers,
        IReadOnlyList<string>? subprotocols = null, CancellationToken ct = default);
    Task SendTextAsync(string message, CancellationToken ct = default);
    Task SendBinaryAsync(byte[] data, CancellationToken ct = default);
    Task DisconnectAsync();
}

public record WebSocketMessage(WebSocketMessageDirection Direction, string Content,
    bool IsBinary, DateTimeOffset Timestamp);

public enum WebSocketMessageDirection { Sent, Received }
public enum WebSocketConnectionState { Disconnected, Connecting, Connected, Faulted }
```

**Implementation:** `src/SwebKit.Core/Services/WebSocketClientService.cs`

- Wraps `System.Net.WebSockets.ClientWebSocket` — no third-party dependency
- Uses `Channel<WebSocketMessage>` for the receive pipe; `ReceiveAsync` exposes
  `IAsyncEnumerable<WebSocketMessage>` via `channel.Reader.ReadAllAsync`
- Background receive loop started on `ConnectAsync`; faults the channel on socket error
- `IAsyncDisposable.DisposeAsync` aborts the socket if still open
- Registered as **transient** (each WebSocket panel creates its own instance)

---

## Export/Import Contracts

`src/SwebKit.Core/Abstractions/`

```csharp
public interface ICollectionExporter
{
    Task<byte[]> ExportAsync(Collection collection, ExportOptions options,
        CancellationToken ct = default);
    string DefaultFileExtension { get; }
    string FormatDisplayName { get; }
}

public interface ICollectionImporter
{
    Task<ImportResult> ImportAsync(Stream source, CancellationToken ct = default);
    bool CanImport(string fileExtension, byte[] header);  // format auto-detection
}

public record ExportOptions(bool IncludeEnvironments = true);

public record ImportResult(bool Success, Collection? Collection,
    ApiEnvironment? Environment, string? ErrorMessage);
```

**Implementations:** `src/SwebKit.Core/Services/ApiClient/`

| Class                                                     | Format                     | Notes                                               |
| --------------------------------------------------------- | -------------------------- | --------------------------------------------------- |
| `SwebKitCollectionExporter` / `SwebKitCollectionImporter` | `SwebKitCollectionV1` JSON | Round-trip lossless                                 |
| `PostmanCollectionExporter`                               | Postman v2.1 JSON          | Projection; test scripts omitted                    |
| `PostmanCollectionImporter`                               | Postman v2.1 JSON          | Maps folders/requests/headers/body; ignores `event` |
| `BrunoCollectionExporter`                                 | `.bru` per-request zip     | Export only in Phase 7                              |

**Bundle integration:** `src/SwebKit.Core/Services/ConfigurationBundleService.cs`

- `ExportAsync` extended to include `ApiClientData` snapshot from `CollectionRepository`
  and `ApiEnvironmentData` snapshot from `EnvironmentRepository`
- `ConfigurationBundleModels.cs` extended with nullable `CollectionsData` and `EnvironmentsData`
  (backward-compatible — existing bundles without these fields restore cleanly)

---

## DI Registrations (MauiProgram.cs additions summary)

```csharp
// Repositories (singleton — shared app state)
builder.Services.AddSingleton<CollectionRepository>();
builder.Services.AddSingleton<EnvironmentRepository>();

// Scoped — per Blazor scope
builder.Services.AddScoped<IHttpRequestExecutor, HttpRequestExecutor>();
builder.Services.AddScoped<IVariableSubstitutionService, VariableSubstitutionService>();
builder.Services.AddScoped<IVariablePreviewService, VariablePreviewService>();
builder.Services.AddScoped<IPostRequestCaptureExecutor, PostRequestCaptureExecutor>();
builder.Services.AddScoped<IAuthInheritanceResolver, AuthInheritanceResolver>();
builder.Services.AddScoped<OAuth2TokenManager>();

// Key Vault (conditional registration)
if (appConfig.KeyVault?.Url is not null)
    builder.Services.AddScoped<IKeyVaultSecretResolver, AzureKeyVaultSecretResolver>();
else
    builder.Services.AddScoped<IKeyVaultSecretResolver, NoopKeyVaultSecretResolver>();

// WebSocket (transient — one per panel/subscription instance)
builder.Services.AddTransient<IWebSocketClientService, WebSocketClientService>();
builder.Services.AddTransient<IGraphQlSubscriptionService, GraphQlSubscriptionService>();

// Export/Import
builder.Services.AddSingleton<ICollectionExporter, SwebKitCollectionExporter>();
builder.Services.AddSingleton<ICollectionExporter, PostmanCollectionExporter>();
builder.Services.AddSingleton<ICollectionExporter, BrunoCollectionExporter>();
builder.Services.AddSingleton<ICollectionImporter, SwebKitCollectionImporter>();
builder.Services.AddSingleton<ICollectionImporter, PostmanCollectionImporter>();

// Named HttpClient for request execution
builder.Services.AddHttpClient("ApiClient");
```

---

## New NuGet Packages Required

| Package                                                                               | Project         | Phase |
| ------------------------------------------------------------------------------------- | --------------- | ----- |
| `Azure.Security.KeyVault.Secrets`                                                     | `SwebKit.Azure` | 3     |
| `JsonPath.Net` (json-everything suite)                                                | `SwebKit.Core`  | 3     |
| _(no new packages for OAuth2 — uses `Microsoft.Maui.Authentication` already in MAUI)_ | —               | 4     |
