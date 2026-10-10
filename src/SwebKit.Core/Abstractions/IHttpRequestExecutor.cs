using SwebKit.Core.Domain;

namespace SwebKit.Core.Abstractions;

/// <summary>
/// Executes an HTTP request and returns the result.
/// Implementations must honour the provided <see cref="CancellationToken"/>.
/// </summary>
public interface IHttpRequestExecutor
{
    /// <summary>
    /// Sends the request after substituting variables in the URL, headers, and body.
    /// Never throws for HTTP-level errors (4xx, 5xx) — those are returned in the result.
    /// Only throws for hard infrastructure failures (network unavailable, etc.) or cancellation.
    /// </summary>
    /// <param name="activeEnvironment">
    /// The collection-scoped environment layer. Overrides <paramref name="globalEnvironment"/>
    /// on a key clash.
    /// </param>
    /// <param name="globalEnvironment">
    /// The global environment layer, applied underneath <paramref name="activeEnvironment"/> so
    /// a value shared by a family of environments need only be defined once.
    /// </param>
    /// <param name="overlay">
    /// Run-scoped variable bag (api-request-chains): applied at TOP priority after every
    /// persisted layer, so a variable captured by an earlier step in the same run wins over
    /// collection and environment definitions — including entries living in other collections.
    /// <see langword="null"/> outside of a run.
    /// </param>
    Task<HttpRequestResult> ExecuteAsync(
        HttpRequestEntry request,
        ApiCollection collection,
        ApiEnvironment? activeEnvironment,
        ApiEnvironment? globalEnvironment = null,
        IReadOnlyDictionary<string, string?>? overlay = null,
        CancellationToken cancellationToken = default);
}
