using SwebKit.Core.Domain;

namespace SwebKit.Core.Abstractions;

/// <summary>
/// Read-only snapshot of an API Client request, with secrets masked.
/// Used by agent tools to describe requests without exposing credentials.
/// </summary>
public sealed class ApiRequestSnapshot
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string CollectionId { get; init; }
    public required string CollectionName { get; init; }
    public required string CollectionOrigin { get; init; } // "local" or "linked"
    public required string? LinkedRootId { get; init; }
    public required string? FolderPath { get; init; }
    public required ApiRequestMethod Method { get; init; }
    public required string Url { get; init; }
    public IReadOnlyList<(string Key, string? Value)> Headers { get; init; } = [];
    public IReadOnlyList<(string Key, string? Value)> QueryParams { get; init; } = [];
    public string? BodyContentType { get; init; }
    public string? BodyPreview { get; init; }
    public string? AuthType { get; init; }
    /// <summary>Response-capture rules on the request — the chaining mechanism that writes
    /// response values (body JSONPath, headers, status) into <c>{{variables}}</c>.</summary>
    public IReadOnlyList<CaptureRule> CaptureRules { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Optional request details beyond the identity/method/URL triplet. On create, every provided
/// value lands on the new request; on update, a non-<c>null</c> field replaces the existing
/// value wholesale (lists are not merged). <see cref="AuthConfig.CredentialSecret"/> must
/// never arrive on <see cref="Auth"/> — callers resolve plaintext secrets into a
/// credential-store key first.
/// </summary>
public sealed class ApiRequestDetails
{
    public IReadOnlyList<KeyValuePair<string>>? Headers { get; init; }
    public IReadOnlyList<KeyValuePair<string>>? QueryParams { get; init; }
    public RequestBody? Body { get; init; }
    public AuthConfig? Auth { get; init; }
    public IReadOnlyList<CaptureRule>? CaptureRules { get; init; }
    public string? GraphQlQuery { get; init; }
    public string? GraphQlVariables { get; init; }
    public string? GraphQlSelectedOperation { get; init; }
    public string? WsSubProtocol { get; init; }
}

/// <summary>
/// Summary of a collection/folder/request for search results.
/// </summary>
public sealed class ApiRequestSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string CollectionId { get; init; }
    public required string CollectionName { get; init; }
    public required string CollectionOrigin { get; init; }
    public required string? LinkedRootId { get; init; }
    public required string? FolderPath { get; init; }
    public required ApiRequestMethod Method { get; init; }
    public required string Url { get; init; }
}

/// <summary>
/// Structure of one collection as the agent sees it: identity plus enough shape
/// (folder paths, request count) for the model to target a create/update without
/// guessing IDs it was never shown.
/// </summary>
public sealed class ApiCollectionSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Origin { get; init; } // "local" or "linked"
    public required string? LinkedRootId { get; init; }
    /// <summary>Every folder path in the collection, '/'-separated (e.g. "Auth/OAuth").</summary>
    public IReadOnlyList<string> FolderPaths { get; init; } = [];
    public required int RequestCount { get; init; }
}

/// <summary>
/// Result of a mutation operation on the API Client store.
/// </summary>
public sealed class ApiClientMutationResult
{
    public required bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public string? RequestId { get; init; }
    public string? CollectionId { get; init; }
}

/// <summary>
/// Core contract for API Client operations, independent of Blazor and page-scoped state.
/// Both the API Client page and agent tools consume this service to avoid divergent implementations.
/// </summary>
public interface IApiClientAgentService
{
    /// <summary>Lists all requests across all collections (local + linked), with stable IDs and origin.</summary>
    Task<IReadOnlyList<ApiRequestSummary>> SearchRequestsAsync(string? query = null, CancellationToken ct = default);

    /// <summary>Reads a single request by ID with secrets masked. Returns null if not found.</summary>
    Task<ApiRequestSnapshot?> GetRequestAsync(string requestId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new request in the specified collection (or root if folderPath is null).
    /// <paramref name="collectionIdOrName"/> resolves by ID first, then by exact name —
    /// agent proposals routinely carry a name because that is all the read tools surface.
    /// When nothing matches, a new local collection is created under that name; a value that
    /// looks like a generated store ID (32 hex chars) still fails instead of creating a
    /// collection named after a stale ID. Missing folder segments are created along the path.
    /// </summary>
    Task<ApiClientMutationResult> CreateRequestAsync(
        string collectionIdOrName,
        string? folderPath,
        string name,
        ApiRequestMethod method,
        string url,
        ApiRequestDetails? details = null,
        CancellationToken ct = default);

    /// <summary>Updates an existing request's name, method, URL, and any provided
    /// <paramref name="details"/> fields (headers, query params, body, auth, capture rules,
    /// protocol payloads). A <c>null</c> detail field leaves the existing value untouched.</summary>
    Task<ApiClientMutationResult> UpdateRequestAsync(
        string requestId,
        string? name = null,
        ApiRequestMethod? method = null,
        string? url = null,
        ApiRequestDetails? details = null,
        CancellationToken ct = default);

    /// <summary>Duplicates an existing request with "(copy)" suffix.</summary>
    Task<ApiClientMutationResult> DuplicateRequestAsync(string requestId, CancellationToken ct = default);

    /// <summary>Moves a request to a new position within the same collection.</summary>
    Task<ApiClientMutationResult> MoveRequestAsync(
        string requestId,
        string? targetFolderPath,
        int? newIndex,
        CancellationToken ct = default);

    /// <summary>Renames a folder within a collection.</summary>
    Task<ApiClientMutationResult> RenameFolderAsync(
        string collectionId,
        string folderPath,
        string newName,
        CancellationToken ct = default);

    /// <summary>Deletes a request or folder (recursive for folders).</summary>
    Task<ApiClientMutationResult> DeleteRequestAsync(string requestId, CancellationToken ct = default);

    /// <summary>Deletes a folder and all its descendants.</summary>
    Task<ApiClientMutationResult> DeleteFolderAsync(
        string collectionId,
        string folderPath,
        CancellationToken ct = default);

    /// <summary>Lists all collections with origin, folder structure, and request counts.</summary>
    Task<IReadOnlyList<ApiCollectionSummary>> GetCollectionsAsync(CancellationToken ct = default);
}

/// <summary>
/// Event published when API Client data changes due to an agent mutation.
/// The open API Client page subscribes to reload affected data.
/// </summary>
public sealed class ApiClientDataChanged
{
    public required string CollectionId { get; init; }
    public required string? RequestId { get; init; }
    public required string ChangeType { get; init; } // "create", "update", "delete", "move"
}
