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
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
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
    public bool IsEnabled { get; set; } = true;
}

// ─── Environments ────────────────────────────────────────────────────────────

/// <summary>Named environment containing a set of variables (possibly secret-backed).</summary>
public sealed class ApiEnvironment
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
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
}

// ─── Authentication ───────────────────────────────────────────────────────────

/// <summary>
/// Auth configuration attached to a collection, folder, or individual request.
/// A request with <c>null</c> auth inherits from its nearest ancestor.
/// </summary>
public sealed class AuthConfig
{
    public AuthType Type { get; set; } = AuthType.None;
    /// <summary>Reference key into <see cref="SwebKit.Core.Abstractions.ICredentialStore"/>. Never contains the actual secret.</summary>
    public string? CredentialKey { get; set; }
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
    /// <summary>ID of the currently active environment. <c>null</c> means "no environment / collection variables only".</summary>
    public string? ActiveEnvironmentId { get; set; }
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

/// <summary>Root object stored in <c>environments.json</c>.</summary>
public sealed class EnvironmentsStore
{
    public int SchemaVersion { get; set; } = 1;
    public List<ApiEnvironment> Environments { get; set; } = [];
    public ApiClientUiState UiState { get; set; } = new();
}
