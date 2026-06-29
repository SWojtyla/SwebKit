using System.Text;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Services;

/// <summary>
/// Shared URL-assembly helper used by <see cref="HttpRequestExecutor"/> and
/// <see cref="ApiClientWorkflowService"/> to avoid duplicating the
/// base-URL + query-parameter merging logic.
/// </summary>
internal static class UrlBuilder
{
    /// <summary>
    /// Substitutes variables in <paramref name="request"/>'s URL, then appends any
    /// enabled query parameters (also variable-substituted and percent-encoded).
    /// </summary>
    public static string Build(
        HttpRequestEntry request,
        IReadOnlyDictionary<string, string?> scope,
        IVariableSubstitutionService substitution)
    {
        var baseUrl = substitution.Substitute(request.Url, scope);

        var enabledParams = request.QueryParams
            .Where(static p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key))
            .ToList();

        if (enabledParams.Count == 0)
            return baseUrl;

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
}
