using SwebKit.Core.Domain;
using SwebKit.DevOps.Authentication;

namespace SwebKit.DevOps;

/// <summary>
/// DelegatingHandler that injects Azure DevOps authorization using the configured authentication mode.
/// Supports PAT (Basic) and Microsoft Entra (Bearer). Tokens/PATs are never logged or exposed.
/// </summary>
public class DevOpsAuthHandler : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<string> PatCredentialKeyOption = new("SwebKit.DevOps.PatCredentialKey");
    public static readonly HttpRequestOptionsKey<string> AuthModeOption = new("SwebKit.DevOps.AuthMode");
    public static readonly HttpRequestOptionsKey<string> OrganizationUrlOption = new("SwebKit.DevOps.OrganizationUrl");

    private readonly IEnumerable<IAuthenticationTokenProvider> _providers;

    public DevOpsAuthHandler(IEnumerable<IAuthenticationTokenProvider> providers)
    {
        _providers = providers;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Options.TryGetValue(OrganizationUrlOption, out var organizationUrl)
            || string.IsNullOrWhiteSpace(organizationUrl))
        {
            organizationUrl = request.RequestUri?.GetLeftPart(UriPartial.Authority) ?? string.Empty;
        }

        _ = request.Options.TryGetValue(PatCredentialKeyOption, out var patCredentialKey);

        if (!request.Options.TryGetValue(AuthModeOption, out var authModeName)
            || string.IsNullOrWhiteSpace(authModeName))
        {
            authModeName = string.IsNullOrWhiteSpace(patCredentialKey)
                ? nameof(DevOpsAuthenticationMode.Entra)
                : nameof(DevOpsAuthenticationMode.Pat);
        }

        var provider = _providers.FirstOrDefault(p => string.Equals(p.Name, authModeName, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            // No provider registered for the requested mode; fall through and let the request
            // continue unauthenticated so callers receive a meaningful 401 from ADO.
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var header = await provider.GetAuthorizationHeaderAsync(organizationUrl, patCredentialKey, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(header))
        {
            var parts = header.Split(' ', 2);
            if (parts.Length == 2)
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(parts[0], parts[1]);
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
