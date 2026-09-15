using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Default implementation of <see cref="IHttpRequestExecutor"/>.
/// Uses a named <see cref="HttpClient"/> ("ApiClient") resolved from <see cref="IHttpClientFactory"/>.
/// Variable substitution is applied to the URL, query string, headers, and body before sending.
/// Auth headers are applied via <see cref="IAuthHeaderBuilder"/> after auth inheritance resolution,
/// against the same variable scope as the rest of the request.
/// Post-request capture rules are applied after a successful response.
/// </summary>
public sealed class HttpRequestExecutor(
    IHttpClientFactory httpClientFactory,
    IVariableSubstitutionService substitution,
    IPostRequestCaptureExecutor captureExecutor,
    IAuthInheritanceResolver authResolver,
    IAuthHeaderBuilder authHeaderBuilder) : IHttpRequestExecutor
{
    public const string ClientName = "ApiClient";

    /// <inheritdoc />
    public async Task<HttpRequestResult> ExecuteAsync(
        HttpRequestEntry request,
        ApiCollection collection,
        ApiEnvironment? activeEnvironment,
        ApiEnvironment? globalEnvironment = null,
        CancellationToken cancellationToken = default)
    {
        // Lowest priority first: the collection-scoped environment overrides the global one.
        var scope = await substitution
            .BuildScopeAsync(collection.Variables, [globalEnvironment, activeEnvironment], cancellationToken)
            .ConfigureAwait(false);

        // Build the URL (with query params merged in)
        var url = UrlBuilder.Build(request, scope, substitution);

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = httpClientFactory.CreateClient(ClientName);
            using var httpRequest = BuildHttpRequest(request, url, scope);

            // Apply resolved auth (request → folder → collection chain)
            var (resolvedAuth, _) = authResolver.Resolve(request, collection);
            await authHeaderBuilder.ApplyAsync(httpRequest, resolvedAuth, scope, cancellationToken).ConfigureAwait(false);

            // Snapshot here and nowhere earlier: this is the last point before the request is
            // handed to the socket, so it is the only place that sees auth headers and the body's
            // content headers together.
            var sentHeaders = CollectSentHeaders(httpRequest);

            using var response = await client.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            sw.Stop();

            var result = await BuildResultAsync(response, url, request.Method.ToString().ToUpperInvariant(), sw.Elapsed, cancellationToken).ConfigureAwait(false);
            result.SentHeaders = sentHeaders;

            // Parse GraphQL errors from the response body when the method is GraphQL
            if (request.Method == ApiRequestMethod.GraphQl && result.ResponseBody is not null)
                result.GraphQlErrors = ParseGraphQlErrors(result.ResponseBody);

            // Apply post-request capture rules (mutates collection/environment in place)
            var captureWarnings = await captureExecutor.ExecuteAsync(result, request, collection, activeEnvironment, cancellationToken).ConfigureAwait(false);
            if (captureWarnings.Count > 0)
                result.CaptureWarnings = captureWarnings;

            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            sw.Stop();
            return new HttpRequestResult
            {
                ResolvedUrl = url,
                Method = request.Method.ToString().ToUpperInvariant(),
                Elapsed = sw.Elapsed,
                ErrorMessage = ex.Message,
            };
        }
    }

    /// <summary>Request headers plus the content headers, which live on separate collections.</summary>
    private static List<(string Name, string Value)> CollectSentHeaders(HttpRequestMessage message)
    {
        var headers = message.Headers
            .Select(h => (h.Key, Values: string.Join(", ", h.Value)));

        if (message.Content is not null)
        {
            headers = headers.Concat(message.Content.Headers
                .Select(h => (h.Key, Values: string.Join(", ", h.Value))));
        }

        return headers.Select(h => (h.Key, h.Values)).ToList();
    }

    // ── Request building ───────────────────────────────────────────────────────

    private HttpRequestMessage BuildHttpRequest(
        HttpRequestEntry request,
        string resolvedUrl,
        IReadOnlyDictionary<string, string?> scope)
    {
        var method = MapMethod(request.Method);
        var msg = new HttpRequestMessage(method, resolvedUrl);

        // Body first — for GraphQL, built from the structured fields, not the raw body.
        //
        // Order matters: a content header (`Content-Type` above all) is rejected by
        // `msg.Headers` and belongs on the content instead. While this ran before the body
        // was assigned, that fallback dereferenced a null `msg.Content` and did nothing, so
        // every user-set Content-Type was silently dropped and the body mode's own
        // `application/json; charset=utf-8` went out instead — producing a request that
        // matched neither the user's headers nor the cURL preview shown beside it.
        msg.Content = request.Method == ApiRequestMethod.GraphQl
            ? BuildGraphQlContent(request, scope)
            : BuildContent(request.Body, scope);

        foreach (var h in request.Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)))
        {
            var value = substitution.Substitute(h.Value ?? string.Empty, scope);
            if (msg.Headers.TryAddWithoutValidation(h.Key, value))
                continue;

            // A content header. Replace rather than add: the body mode already set a default
            // Content-Type, and appending would send the header twice.
            if (msg.Content is not null)
            {
                msg.Content.Headers.Remove(h.Key);
                msg.Content.Headers.TryAddWithoutValidation(h.Key, value);
            }
        }

        return msg;
    }

    private HttpContent? BuildContent(RequestBody body, IReadOnlyDictionary<string, string?> scope)
    {
        switch (body.Mode)
        {
            case RequestBodyMode.None:
                return null;

            case RequestBodyMode.Json:
                {
                    var raw = substitution.Substitute(body.RawContent ?? string.Empty, scope);
                    var content = new StringContent(raw, Encoding.UTF8);
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                    return content;
                }

            case RequestBodyMode.Xml:
                {
                    var raw = substitution.Substitute(body.RawContent ?? string.Empty, scope);
                    var content = new StringContent(raw, Encoding.UTF8);
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/xml") { CharSet = "utf-8" };
                    return content;
                }

            case RequestBodyMode.Text:
                {
                    var raw = substitution.Substitute(body.RawContent ?? string.Empty, scope);
                    var ct = body.ContentType ?? "text/plain";
                    var content = new StringContent(raw, Encoding.UTF8);
                    content.Headers.ContentType = new MediaTypeHeaderValue(ct) { CharSet = "utf-8" };
                    return content;
                }

            case RequestBodyMode.FormData:
                {
                    var form = new MultipartFormDataContent();
                    foreach (var kv in body.FormData.Where(f => f.IsEnabled && !string.IsNullOrWhiteSpace(f.Key)))
                    {
                        var val = substitution.Substitute(kv.Value ?? string.Empty, scope);
                        form.Add(new StringContent(val), kv.Key);
                    }
                    return form;
                }

            case RequestBodyMode.Binary when body.FilePath is not null && File.Exists(body.FilePath):
                {
                    var bytes = File.ReadAllBytes(body.FilePath);
                    var content = new ByteArrayContent(bytes);
                    content.Headers.ContentType = new MediaTypeHeaderValue(body.ContentType ?? "application/octet-stream");
                    return content;
                }

            default:
                return null;
        }
    }

    private HttpContent BuildGraphQlContent(HttpRequestEntry request, IReadOnlyDictionary<string, string?> scope)
    {
        // Substitute variables inside the query and variables JSON
        var query = substitution.Substitute(request.GraphQlQuery ?? string.Empty, scope);
        var variablesRaw = string.IsNullOrWhiteSpace(request.GraphQlVariables)
            ? null
            : substitution.Substitute(request.GraphQlVariables, scope);

        object? variables = null;
        if (!string.IsNullOrWhiteSpace(variablesRaw))
        {
            try { variables = JsonNode.Parse(variablesRaw); }
            catch (System.Text.Json.JsonException) { /* invalid JSON — omit variables */ }
        }

        var operationName = string.IsNullOrWhiteSpace(request.GraphQlSelectedOperation)
            ? null
            : request.GraphQlSelectedOperation;

        var payload = new Dictionary<string, object?> { ["query"] = query };
        if (variables is not null) payload["variables"] = variables;
        if (operationName is not null) payload["operationName"] = operationName;

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return content;
    }

    private static IReadOnlyList<GraphQlError>? ParseGraphQlErrors(string responseBody)
    {
        try
        {
            var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("errors", out var errorsElement) ||
                errorsElement.ValueKind != JsonValueKind.Array)
                return null;

            var errors = new List<GraphQlError>();
            foreach (var errorEl in errorsElement.EnumerateArray())
            {
                var message = errorEl.TryGetProperty("message", out var msgEl)
                    ? msgEl.GetString() ?? "Unknown GraphQL error"
                    : "Unknown GraphQL error";

                List<GraphQlErrorLocation>? locations = null;
                if (errorEl.TryGetProperty("locations", out var locsEl) &&
                    locsEl.ValueKind == JsonValueKind.Array)
                {
                    locations = [];
                    foreach (var loc in locsEl.EnumerateArray())
                    {
                        var line = loc.TryGetProperty("line", out var lineEl) ? lineEl.GetInt32() : 0;
                        var col = loc.TryGetProperty("column", out var colEl) ? colEl.GetInt32() : 0;
                        locations.Add(new GraphQlErrorLocation { Line = line, Column = col });
                    }
                }

                List<string>? path = null;
                if (errorEl.TryGetProperty("path", out var pathEl) &&
                    pathEl.ValueKind == JsonValueKind.Array)
                {
                    path = [];
                    foreach (var seg in pathEl.EnumerateArray())
                        path.Add(seg.ToString());
                }

                errors.Add(new GraphQlError { Message = message, Locations = locations, Path = path });
            }

            return errors.Count > 0 ? errors : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // ── Response reading ───────────────────────────────────────────────────────

    private static async Task<HttpRequestResult> BuildResultAsync(
        HttpResponseMessage response,
        string resolvedUrl,
        string method,
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        var headers = response.Headers
            .Concat(response.Content.Headers)
            .SelectMany(h => h.Value.Select(v => (h.Key, v)))
            .ToList();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var contentLength = response.Content.Headers.ContentLength ?? -1;
        var isBinary = IsBinaryContentType(contentType);

        string? body = null;
        byte[]? bodyBytes = null;
        var truncated = false;

        try
        {
            if (isBinary)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                if (bytes.Length > HttpRequestResult.ResponseBodyMaxBytes)
                {
                    bodyBytes = bytes[..HttpRequestResult.ResponseBodyMaxBytes];
                    truncated = true;
                }
                else
                {
                    bodyBytes = bytes;
                }
                body = Convert.ToHexString(bodyBytes);
            }
            else
            {
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var limited = new LimitedStream(stream, HttpRequestResult.ResponseBodyMaxBytes);
                body = await new StreamReader(limited, Encoding.UTF8).ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                truncated = limited.WasTruncated;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (IOException)
        {
            // Swallow body read errors — return whatever we have
        }

        return new HttpRequestResult
        {
            ResolvedUrl = resolvedUrl,
            Method = method,
            StatusCode = (int)response.StatusCode,
            StatusText = $"{(int)response.StatusCode} {response.ReasonPhrase}",
            Elapsed = elapsed,
            ContentLength = contentLength,
            ContentType = contentType,
            ResponseHeaders = headers,
            ResponseBody = body,
            ResponseBodyBytes = bodyBytes,
            ResponseBodyTruncated = truncated,
        };
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static HttpMethod MapMethod(ApiRequestMethod method) => method switch
    {
        ApiRequestMethod.Get => HttpMethod.Get,
        ApiRequestMethod.Post => HttpMethod.Post,
        ApiRequestMethod.Put => HttpMethod.Put,
        ApiRequestMethod.Patch => HttpMethod.Patch,
        ApiRequestMethod.Delete => HttpMethod.Delete,
        ApiRequestMethod.Head => HttpMethod.Head,
        ApiRequestMethod.Options => HttpMethod.Options,
        // GraphQL and WebSocket fall back to POST for the HTTP transport
        ApiRequestMethod.GraphQl => HttpMethod.Post,
        _ => HttpMethod.Get,
    };

    private static bool IsBinaryContentType(string? contentType)
    {
        if (contentType is null) return false;
        return contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
               contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ||
               contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
               contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase) ||
               contentType.Equals("application/zip", StringComparison.OrdinalIgnoreCase) ||
               contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    // ── Nested helper: stream limiter ──────────────────────────────────────────

    /// <summary>
    /// Wraps a stream and stops reading after <paramref name="maxBytes"/> bytes.
    /// Check <see cref="WasTruncated"/> after reading completes.
    /// </summary>
    private sealed class LimitedStream(Stream inner, int maxBytes) : Stream
    {
        private int _read;

        public bool WasTruncated { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = maxBytes - _read;
            if (remaining <= 0) { WasTruncated = true; return 0; }

            var toRead = Math.Min(count, remaining);
            var actual = inner.Read(buffer, offset, toRead);
            _read += actual;
            if (_read >= maxBytes) WasTruncated = true;
            return actual;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var remaining = maxBytes - _read;
            if (remaining <= 0) { WasTruncated = true; return 0; }

            var toRead = Math.Min(buffer.Length, remaining);
            var actual = await inner.ReadAsync(buffer[..toRead], ct).ConfigureAwait(false);
            _read += actual;
            if (_read >= maxBytes) WasTruncated = true;
            return actual;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
