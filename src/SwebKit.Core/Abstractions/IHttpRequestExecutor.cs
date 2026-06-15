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
    Task<HttpRequestResult> ExecuteAsync(
        HttpRequestEntry request,
        ApiCollection collection,
        ApiEnvironment? activeEnvironment,
        CancellationToken cancellationToken = default);
}
