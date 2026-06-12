namespace SwebKit.Core.Domain;

/// <summary>
/// The outcome of executing a single HTTP request via <see cref="SwebKit.Core.Abstractions.IHttpRequestExecutor"/>.
/// HTTP-level errors (4xx, 5xx) are represented as successful results with an error status code —
/// only infrastructure failures produce a thrown exception.
/// </summary>
public sealed class HttpRequestResult
{
    // ── Request echo ──────────────────────────────────────────────────────────

    /// <summary>The URL that was actually sent (after variable substitution).</summary>
    public string ResolvedUrl { get; init; } = string.Empty;

    /// <summary>The HTTP method that was used (as a string for display).</summary>
    public string Method { get; init; } = string.Empty;

    // ── Response metadata ─────────────────────────────────────────────────────

    /// <summary>HTTP status code returned by the server, or <c>0</c> if the request never reached it.</summary>
    public int StatusCode { get; init; }

    /// <summary>Human-readable status text (e.g., "200 OK", "404 Not Found").</summary>
    public string StatusText { get; init; } = string.Empty;

    /// <summary>Total elapsed time for the request+response round-trip.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Number of bytes in the response body as reported by <c>Content-Length</c>, or -1 if unknown.</summary>
    public long ContentLength { get; init; } = -1;

    // ── Response headers ──────────────────────────────────────────────────────

    public IReadOnlyList<(string Name, string Value)> ResponseHeaders { get; init; } = [];

    // ── Response body ─────────────────────────────────────────────────────────

    /// <summary>
    /// Response body as text. Binary responses are hex-encoded.
    /// May be truncated when the response body exceeds <see cref="ResponseBodyMaxBytes"/>.
    /// </summary>
    public string? ResponseBody { get; init; }

    /// <summary>Raw response body bytes — populated for binary content types.</summary>
    public byte[]? ResponseBodyBytes { get; init; }

    /// <summary>
    /// <c>true</c> when the response body was truncated because it exceeded
    /// the <see cref="ResponseBodyMaxBytes"/> safety limit.
    /// </summary>
    public bool ResponseBodyTruncated { get; init; }

    /// <summary>Content-Type header value, used to choose the response body renderer.</summary>
    public string? ContentType { get; init; }

    // ── Error ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Non-null when the request failed at the infrastructure level (DNS, connection refused,
    /// TLS error, timeout, etc.) rather than returning an HTTP error response.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// <c>true</c> for HTTP-level success (2xx) when <see cref="ErrorMessage"/> is null.
    /// </summary>
    public bool IsSuccess => ErrorMessage is null && StatusCode is >= 200 and < 300;

    // ── Capture warnings ──────────────────────────────────────────────────────

    /// <summary>
    /// Non-empty when one or more post-request capture rules failed to match.
    /// Each entry is a human-readable message describing the failure.
    /// </summary>
    public IReadOnlyList<string> CaptureWarnings { get; set; } = [];

    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>Maximum response body size buffered and displayed (4 MB).</summary>
    public const int ResponseBodyMaxBytes = 4 * 1024 * 1024;
}
