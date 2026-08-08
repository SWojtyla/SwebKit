namespace SwebKit.Core.Domain;

// ─── Collection hierarchy ────────────────────────────────────────────────────

/// <summary>Top-level named collection that owns a tree of folders and requests.</summary>
public sealed class ApiCollection
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<ApiCollectionNode> Nodes { get; set; } = [];
    public List<CollectionVariable> Variables { get; set; } = [];
    public AuthConfig? DefaultAuth { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A node in the collection tree — either a folder or a request.</summary>
public sealed class ApiCollectionNode
{
    public string Id { get; set; } = string.Empty;
    public ApiCollectionNodeType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsExpanded { get; set; } = true;

    /// <summary>Child nodes — populated only when <see cref="Type"/> is <see cref="ApiCollectionNodeType.Folder"/>.</summary>
    public List<ApiCollectionNode> Children { get; set; } = [];

    /// <summary>Folder-level auth that child requests inherit when they have no auth of their own.</summary>
    public AuthConfig? DefaultAuth { get; set; }

    /// <summary>Request entry — populated only when <see cref="Type"/> is <see cref="ApiCollectionNodeType.Request"/>.</summary>
    public HttpRequestEntry? Request { get; set; }
}

public enum ApiCollectionNodeType
{
    Folder,
    Request,
}

// ─── Request entry ───────────────────────────────────────────────────────────

/// <summary>A single HTTP/GraphQL/WebSocket request stored in a collection.</summary>
public sealed class HttpRequestEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ApiRequestMethod Method { get; set; } = ApiRequestMethod.Get;
    public string Url { get; set; } = string.Empty;
    public List<KeyValuePair<string>> Headers { get; set; } = [];
    public List<KeyValuePair<string>> QueryParams { get; set; } = [];
    public RequestBody Body { get; set; } = new();
    public AuthConfig? Auth { get; set; }
    public List<CaptureRule> CaptureRules { get; set; } = [];

    // ─── GraphQL fields ──────────────────────────────────────────────────────
    /// <summary>GraphQL query or mutation document. Used when <see cref="Method"/> is <see cref="ApiRequestMethod.GraphQl"/>.</summary>
    public string? GraphQlQuery { get; set; }
    /// <summary>GraphQL variables as a JSON string. May be null or empty when there are no variables.</summary>
    public string? GraphQlVariables { get; set; }
    /// <summary>
    /// Name of the operation to execute when the document contains multiple named operations.
    /// Null means "execute the only/first operation".
    /// </summary>
    public string? GraphQlSelectedOperation { get; set; }

    // ─── WebSocket fields ─────────────────────────────────────────────────────
    /// <summary>
    /// Named message templates the user has saved for this request.
    /// Displayed in the composer's "Saved Messages" dropdown.
    /// </summary>
    public List<WebSocketSavedMessage> SavedMessages { get; set; } = [];

    /// <summary>
    /// Optional WebSocket subprotocol sent in the <c>Sec-WebSocket-Protocol</c> upgrade header.
    /// </summary>
    public string? WsSubProtocol { get; set; }
    public List<ResponseExample> ResponseExamples { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<RequestAction> PreRequestActions { get; set; } = [];
    public List<RequestAction> PostRequestActions { get; set; } = [];
}

public sealed class RequestAction
{
    public string Id { get; set; } = string.Empty;
    public RequestActionKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public RequestActionSource Source { get; set; } = RequestActionSource.RequestUrl;
    public string? Selector { get; set; }
    public int DelayMs { get; set; }
}

public enum RequestActionKind
{
    CopyToClipboard,
    Delay,
}

public enum RequestActionSource
{
    RequestUrl,
    RequestMethod,
    RequestBody,
    ResponseStatusCode,
    ResponseStatusText,
    ResponseBody,
    ResponseHeader,
}

public sealed class ResponseExample
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public string? Body { get; set; }
    public List<KeyValuePair<string>> Headers { get; set; } = [];
    public DateTimeOffset CapturedAt { get; set; }
    public string? EnvironmentName { get; set; }
}

public sealed class VariableInspectionItem
{
    public string Key { get; init; } = string.Empty;
    public VariableInspectionSource Source { get; init; } = VariableInspectionSource.Unresolved;
    public string? DisplayValue { get; init; }
    public bool IsSecret { get; init; }
    public bool IsResolved => Source != VariableInspectionSource.Unresolved && DisplayValue is not null;
}

public enum VariableInspectionSource
{
    Collection,
    Environment,
    Generated,
    CredentialStore,
    KeyVault,
    Unresolved,
}

public sealed class CurlImportResult
{
    public bool IsSuccess { get; init; }
    public HttpRequestEntry? Request { get; init; }
    public string? ErrorMessage { get; init; }

    public static CurlImportResult Success(HttpRequestEntry request) => new() { IsSuccess = true, Request = request };

    public static CurlImportResult Failure(string errorMessage) => new() { ErrorMessage = errorMessage };
}

public enum ApiRequestMethod
{
    Get,
    Post,
    Put,
    Patch,
    Delete,
    Head,
    Options,
    GraphQl,
    WebSocket,
}

/// <summary>HTTP request body with mode selector.</summary>
public sealed class RequestBody
{
    public RequestBodyMode Mode { get; set; } = RequestBodyMode.None;
    /// <summary>Raw text content (used for JSON, XML, Plain text modes).</summary>
    public string? RawContent { get; set; }
    /// <summary>Content type for the raw body (e.g., "application/json").</summary>
    public string? ContentType { get; set; }
    public List<KeyValuePair<string>> FormData { get; set; } = [];
    /// <summary>File path for binary uploads.</summary>
    public string? FilePath { get; set; }
}

public enum RequestBodyMode
{
    None,
    Json,
    Xml,
    Text,
    FormData,
    Binary,
}

/// <summary>Simple key/value pair where value may be absent (disabled row).</summary>
public sealed class KeyValuePair<T>
{
    public string Key { get; set; } = string.Empty;
    public T? Value { get; set; }
    public bool IsEnabled { get; set; } = true;
}

// ─── Variables ───────────────────────────────────────────────────────────────

/// <summary>A collection-level variable that is always in scope regardless of active environment.</summary>
public sealed class CollectionVariable
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public VariableGeneratorDefinition? Generator { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class VariableGeneratorDefinition
{
    public VariableGeneratorKind Kind { get; set; } = VariableGeneratorKind.Integer;
    public int? MinInt { get; set; } = 1;
    public int? MaxInt { get; set; } = 100;
    public decimal? MinDecimal { get; set; }
    public decimal? MaxDecimal { get; set; }
    public int DecimalPlaces { get; set; } = 2;
    public int? TrueWeightPercent { get; set; }
    public string? FakerCategory { get; set; } = "person.firstName";
    public string? Template { get; set; }
    public List<string> Values { get; set; } = [];
}

public enum VariableGeneratorKind
{
    Integer,
    Decimal,
    Boolean,
    Guid,
    DateTime,
    List,
    Faker,
    Template,
}

// ─── Environments ────────────────────────────────────────────────────────────

/// <summary>Named environment containing a set of variables (possibly secret-backed).</summary>
public sealed class ApiEnvironment
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// ID of the collection this environment is scoped to, or <c>null</c> for a global environment
    /// available to every collection. For local storage this is persisted in <c>environments.json</c>;
    /// for linked repos it is derived from the environment file's location on disk (root vs a
    /// collection's <c>environments/</c> folder) and not stored inside the file.
    /// </summary>
    public string? CollectionId { get; set; }
    public List<EnvironmentVariable> Variables { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A variable inside an environment with optional secret backing.</summary>
public sealed class EnvironmentVariable
{
    public string Key { get; set; } = string.Empty;
    /// <summary>Plain value when <see cref="SecretSource"/> is <see cref="EnvironmentVariableSecretSource.Plain"/>.</summary>
    public string? Value { get; set; }
    public EnvironmentVariableSecretSource SecretSource { get; set; } = EnvironmentVariableSecretSource.Plain;
    public VariableGeneratorDefinition? Generator { get; set; }
    /// <summary>Key used to look up the secret in the Windows Credential Store or Azure Key Vault.</summary>
    public string? CredentialKey { get; set; }
    /// <summary>
    /// The <see cref="KeyVaultEntry.Name"/> of the vault to use when <see cref="SecretSource"/> is
    /// <see cref="EnvironmentVariableSecretSource.AzureKeyVault"/>.
    /// </summary>
    public string? KeyVaultName { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public enum EnvironmentVariableSecretSource
{
    Plain,
    WindowsCredentialStore,
    AzureKeyVault,
    Generated,
}

// ─── Authentication ───────────────────────────────────────────────────────────

/// <summary>
/// Auth configuration attached to a collection, folder, or individual request.
/// A request with <c>null</c> auth inherits from its nearest ancestor.
/// </summary>
public sealed class AuthConfig
{
    public AuthType Type { get; set; } = AuthType.None;
    /// <summary>Reference key into the persisted secret store. Never contains the actual secret.</summary>
    public string? CredentialKey { get; set; }
    /// <summary>Transient secret material provided at execution time. Not persisted when null so <c>collections.json</c> never stores it.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CredentialSecret { get; set; }
    /// <summary>Header or query-param name for API key auth.</summary>
    public string? ApiKeyParamName { get; set; }
    public ApiKeyLocation ApiKeyLocation { get; set; } = ApiKeyLocation.Header;
    /// <summary>Username for Basic auth (non-secret). Password is stored in <see cref="CredentialKey"/>.</summary>
    public string? BasicUsername { get; set; }
    /// <summary>OAuth 2 client identifier (non-secret). Client secret is stored in <see cref="CredentialKey"/>.</summary>
    public string? OAuth2ClientId { get; set; }
    /// <summary>OAuth 2 grant type hint for UI selection.</summary>
    public OAuth2GrantType OAuth2GrantType { get; set; } = OAuth2GrantType.ClientCredentials;
    /// <summary>OAuth 2 token endpoint URL.</summary>
    public string? OAuth2TokenUrl { get; set; }
    /// <summary>OAuth 2 authorization endpoint URL (auth code flow only).</summary>
    public string? OAuth2AuthUrl { get; set; }
    /// <summary>OAuth 2 scopes (space-separated).</summary>
    public string? OAuth2Scopes { get; set; }
}

public enum AuthType
{
    None,
    Inherited,
    BearerToken,
    ApiKey,
    Basic,
    OAuth2,
}

public enum ApiKeyLocation
{
    Header,
    QueryParam,
}

public enum OAuth2GrantType
{
    ClientCredentials,
    AuthorizationCode,
}

// ─── Post-request capture rules ───────────────────────────────────────────────

/// <summary>
/// A no-code rule that extracts a value from the response and stores it in a variable.
/// Source can be the response body (JSONPath), a response header, or the status code.
/// </summary>
public sealed class CaptureRule
{
    public string Id { get; set; } = string.Empty;
    /// <summary>Target variable key to write the captured value into.</summary>
    public string TargetVariable { get; set; } = string.Empty;
    /// <summary>Scope: "collection" stores in the collection's own variable list; any other value is treated as an environment name.</summary>
    public string TargetScope { get; set; } = "collection";
    public CaptureSource Source { get; set; } = CaptureSource.BodyJsonPath;
    /// <summary>JSONPath expression — used when <see cref="Source"/> is <see cref="CaptureSource.BodyJsonPath"/>.</summary>
    public string? JsonPath { get; set; }
    /// <summary>Header name — used when <see cref="Source"/> is <see cref="CaptureSource.ResponseHeader"/>.</summary>
    public string? HeaderName { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public enum CaptureSource
{
    BodyJsonPath,
    ResponseHeader,
    StatusCode,
}

// ─── Active environment state ─────────────────────────────────────────────────

/// <summary>
/// Persisted UI state for the API client: which environment is currently active
/// and the last selected request per collection.
/// </summary>
public sealed class ApiClientUiState
{
    /// <summary>
    /// ID of the currently active environment. <c>null</c> means "no environment / collection variables only".
    /// Used as the global fallback when a collection has no per-collection selection in
    /// <see cref="ActiveEnvironmentIdByCollection"/>.
    /// </summary>
    public string? ActiveEnvironmentId { get; set; }
    /// <summary>Active environment ID, keyed by collection ID. Takes precedence over <see cref="ActiveEnvironmentId"/>.</summary>
    public Dictionary<string, string> ActiveEnvironmentIdByCollection { get; set; } = [];
    /// <summary>Last request selected, keyed by collection ID.</summary>
    public Dictionary<string, string> LastSelectedRequestIdByCollection { get; set; } = [];
}

// ─── Persistence containers ───────────────────────────────────────────────────

/// <summary>Root object stored in <c>collections.json</c>.</summary>
public sealed class CollectionsStore
{
    public int SchemaVersion { get; set; } = 1;
    public List<ApiCollection> Collections { get; set; } = [];
}

/// <summary>Response wrapper for the collections store, including a concurrency token for stale-file detection.</summary>
public sealed class CollectionsStoreResponse
{
    public int SchemaVersion { get; set; } = 1;
    public List<ApiCollection> Collections { get; set; } = [];
    public string? ConcurrencyToken { get; set; }
}

/// <summary>Root object stored in <c>environments.json</c>.</summary>
public sealed class EnvironmentsStore
{
    public int SchemaVersion { get; set; } = 1;
    public List<ApiEnvironment> Environments { get; set; } = [];
    public ApiClientUiState UiState { get; set; } = new();
}

// ─── GraphQL types ────────────────────────────────────────────────────────────

/// <summary>A single error entry from a GraphQL <c>{ "errors": [...] }</c> response.</summary>
public sealed class GraphQlError
{
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<GraphQlErrorLocation>? Locations { get; init; }
    public IReadOnlyList<string>? Path { get; init; }
}

/// <summary>Source location of a GraphQL error.</summary>
public sealed class GraphQlErrorLocation
{
    public int Line { get; init; }
    public int Column { get; init; }
}

/// <summary>A single message received during a GraphQL subscription.</summary>
public sealed class GraphQlSubscriptionMessage
{
    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>The raw JSON payload from the <c>next</c> frame.</summary>
    public string Payload { get; init; } = string.Empty;
    /// <summary>Errors embedded in this message, if any.</summary>
    public IReadOnlyList<GraphQlError>? Errors { get; init; }
}

// ─── WebSocket types ──────────────────────────────────────────────────────────

public enum WebSocketMessageDirection
{
    Sent,
    Received,
}

public enum WebSocketFrameType
{
    Text,
    Binary,
}

/// <summary>A single frame in a WebSocket session log.</summary>
public sealed class WebSocketMessage
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public WebSocketMessageDirection Direction { get; init; }
    public WebSocketFrameType FrameType { get; init; } = WebSocketFrameType.Text;
    /// <summary>Message content. Binary frames are stored as hex-encoded strings.</summary>
    public string Content { get; init; } = string.Empty;
    /// <summary>Byte count of the original frame.</summary>
    public int ByteCount { get; init; }
}

/// <summary>A named message template the user has saved for quick resending.</summary>
public sealed class WebSocketSavedMessage
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public WebSocketFrameType FrameType { get; set; } = WebSocketFrameType.Text;
}

public enum WebSocketConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Faulted,
}
