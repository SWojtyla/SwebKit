using SwebKit.Core.Domain;

namespace SwebKit.Core.Abstractions;

/// <summary>
/// Provides GraphQL schema introspection and document parsing utilities.
/// Schema results are cached per endpoint URL.
/// </summary>
public interface IGraphQlSchemaService
{
    /// <summary>
    /// Sends an introspection query to <paramref name="endpointUrl"/> and caches the schema.
    /// Returns a successful result containing the schema SDL/JSON on success, or an error message on failure.
    /// Never throws — errors are surfaced through <see cref="GraphQlIntrospectionResult.ErrorMessage"/>.
    /// </summary>
    Task<GraphQlIntrospectionResult> IntrospectAsync(
        string endpointUrl,
        ApiCollection collection,
        ApiEnvironment? activeEnvironment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses the names of all named top-level operations in <paramref name="queryDocument"/>.
    /// Returns an empty list when the document has no named operations.
    /// </summary>
    IReadOnlyList<string> ParseOperationNames(string queryDocument);

    /// <summary>
    /// Returns the last cached introspection result for <paramref name="endpointUrl"/>, or <c>null</c>
    /// if the endpoint has not been introspected in this session.
    /// </summary>
    GraphQlIntrospectionResult? GetCachedSchema(string endpointUrl);

    /// <summary>Clears any cached schema for <paramref name="endpointUrl"/>.</summary>
    void ClearCache(string endpointUrl);
}

/// <summary>Outcome of a GraphQL introspection attempt.</summary>
public sealed class GraphQlIntrospectionResult
{
    /// <summary>The raw JSON response body from the introspection query.</summary>
    public string? SchemaJson { get; init; }

    /// <summary>Non-null when introspection failed. The user-visible error message.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>The endpoint URL that was introspected.</summary>
    public string EndpointUrl { get; init; } = string.Empty;

    /// <summary>When the result was retrieved.</summary>
    public DateTimeOffset RetrievedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool IsSuccess => ErrorMessage is null && SchemaJson is not null;
}
