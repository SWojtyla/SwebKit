using System.Text;
using SwebKit.Core.Security;

namespace SwebKit.Sidecar.Services;

/// <summary>
/// POSTs a rendered access-request body to the configured webhook (Phase 4 of
/// access-awareness-pipeline.md) — typically a Teams Power App / Power Automate HTTP
/// trigger URL, which embeds a SAS <c>sig</c> and is therefore pulled from the credential
/// store rather than config.
///
/// Deliberately honest about outcomes: a 2xx means only "the trigger accepted the call",
/// never that access was granted; every failure is reported with its HTTP status (or the
/// reason none arrived) so the UI can show the real result instead of a success-shaped lie.
///
/// Error strings are built by hand rather than from <see cref="HttpRequestException.Message"/>
/// because those messages can embed the request URI — which would leak the SAS signature
/// into logs, notifications, and the response body.
/// </summary>
public sealed class AccessRequestSender(HttpClient http)
{
    /// <summary>The typed-client timeout set in Program.cs — Power Automate triggers answer
    /// fast when healthy; longer waits mean a wedged flow, not more reliability.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Max characters of a failed response's body echoed into <c>Error</c> — enough
    /// to carry a Power Automate error message without dumping an HTML error page.</summary>
    private const int MaxResponseSnippet = 200;

    public async Task<AccessRequestSendResult> SendAsync(
        string url,
        string body,
        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            return new AccessRequestSendResult(
                Sent: false, DryRun: false, Demo: false, StatusCode: null,
                Error: "The stored webhook URL isn't an absolute https:// URL — re-save it in Settings → Access.",
                RenderedBody: body);
        }

        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(uri, content, ct).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                return new AccessRequestSendResult(
                    Sent: true, DryRun: false, Demo: false, StatusCode: status,
                    Error: null, RenderedBody: body);
            }

            var snippet = await ResponseSnippetAsync(response, ct).ConfigureAwait(false);
            return new AccessRequestSendResult(
                Sent: false, DryRun: false, Demo: false, StatusCode: status,
                Error: $"The webhook answered HTTP {status} {response.ReasonPhrase}.{snippet}",
                RenderedBody: body);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient.Timeout fired — the caller's token wasn't the cause.
            return new AccessRequestSendResult(
                Sent: false, DryRun: false, Demo: false, StatusCode: null,
                Error: $"The webhook didn't answer within {Timeout.TotalSeconds:0} seconds.",
                RenderedBody: body);
        }
        catch (HttpRequestException ex)
        {
            return new AccessRequestSendResult(
                Sent: false, DryRun: false, Demo: false, StatusCode: null,
                Error: $"Couldn't reach the webhook ({ex.HttpRequestError}).",
                RenderedBody: body);
        }
    }

    /// <summary>A short excerpt of a non-2xx body for the error line — Power Automate and
    /// Logic Apps put the real rejection reason in a small JSON payload worth surfacing.</summary>
    private static async Task<string> ResponseSnippetAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }
            var trimmed = text.Trim();
            return " " + (trimmed.Length > MaxResponseSnippet
                ? trimmed[..MaxResponseSnippet] + "…"
                : trimmed);
        }
        catch
        {
            // An unreadable body doesn't hide the status — the status alone is still honest.
            return string.Empty;
        }
    }
}
