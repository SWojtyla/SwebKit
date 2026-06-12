using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Default implementation of <see cref="IHttpRequestExecutor"/>.
/// Uses a named <see cref="HttpClient"/> ("ApiClient") resolved from <see cref="IHttpClientFactory"/>.
/// Variable substitution is applied to the URL, query string, headers, and body before sending.
/// </summary>
public sealed class HttpRequestExecutor(
    IHttpClientFactory httpClientFactory,
    IVariableSubstitutionService substitution) : IHttpRequestExecutor
{
    public const string ClientName = "ApiClient";

    /// <inheritdoc />
    public async Task<HttpRequestResult> ExecuteAsync(
        HttpRequestEntry request,
        ApiCollection collection,
        ApiEnvironment? activeEnvironment,
        CancellationToken cancellationToken = default)
    {
        var scope = substitution.BuildScope(collection.Variables, activeEnvironment);

        // Build the URL (with query params merged in)
        var url = BuildUrl(request, scope);

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = httpClientFactory.CreateClient(ClientName);
            using var httpRequest = BuildHttpRequest(request, url, scope);

            using var response = await client.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            sw.Stop();

            return await BuildResultAsync(response, url, request.Method.ToString().ToUpperInvariant(), sw.Elapsed, cancellationToken);
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

    // ── Request building ───────────────────────────────────────────────────────

    private string BuildUrl(HttpRequestEntry request, IReadOnlyDictionary<string, string?> scope)
    {
        var baseUrl = substitution.Substitute(request.Url, scope);

        var enabledParams = request.QueryParams
            .Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key))
            .ToList();

        if (enabledParams.Count == 0) return baseUrl;

        var sb = new StringBuilder(baseUrl);
        sb.Append(baseUrl.Contains('?') ? '&' : '?');

        for (var i = 0; i < enabledParams.Count; i++)
        {
            if (i > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(enabledParams[i].Key));
            if (enabledParams[i].Value is not null)
            {
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(
                    substitution.Substitute(enabledParams[i].Value ?? string.Empty, scope)));
            }
        }

        return sb.ToString();
    }

    private HttpRequestMessage BuildHttpRequest(
        HttpRequestEntry request,
        string resolvedUrl,
        IReadOnlyDictionary<string, string?> scope)
    {
        var method = MapMethod(request.Method);
        var msg = new HttpRequestMessage(method, resolvedUrl);

        // Headers
        foreach (var h in request.Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)))
        {
            var value = substitution.Substitute(h.Value ?? string.Empty, scope);
            if (!msg.Headers.TryAddWithoutValidation(h.Key, value))
                msg.Content?.Headers.TryAddWithoutValidation(h.Key, value);
        }

        // Body
        msg.Content = BuildContent(request.Body, scope);

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
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
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
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var limited = new LimitedStream(stream, HttpRequestResult.ResponseBodyMaxBytes);
                body = await new StreamReader(limited, Encoding.UTF8).ReadToEndAsync(cancellationToken);
                truncated = limited.WasTruncated;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch
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

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            var remaining = maxBytes - _read;
            if (remaining <= 0) { WasTruncated = true; return 0; }

            var toRead = Math.Min(count, remaining);
            var actual = await inner.ReadAsync(buffer.AsMemory(offset, toRead), ct);
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
