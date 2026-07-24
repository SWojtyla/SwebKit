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
    public DateTimeOffset UpdatedAt { get; init; }
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

    /// <summary>Creates a new request in the specified collection (or root if folderPath is null).</summary>
    Task<ApiClientMutationResult> CreateRequestAsync(
        string collectionId,
        string? folderPath,
        string name,
        ApiRequestMethod method,
        string url,
        CancellationToken ct = default);

    /// <summary>Updates an existing request's name, method, URL, headers, query params, or body.</summary>
    Task<ApiClientMutationResult> UpdateRequestAsync(
        string requestId,
        string? name = null,
        ApiRequestMethod? method = null,
        string? url = null,
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

    /// <summary>Lists all collections with their origin (local/linked).</summary>
    Task<IReadOnlyList<(string Id, string Name, string Origin, string? LinkedRootId)>> GetCollectionsAsync(CancellationToken ct = default);
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
