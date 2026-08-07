using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace SwebKit.DevOps.Authentication;

/// <summary>
/// Microsoft Entra token provider for Azure DevOps. Uses <see cref="DefaultAzureCredential"/>
/// and requests a token for the Azure DevOps resource.
/// </summary>
public sealed class EntraTokenProvider : IAuthenticationTokenProvider, IDisposable
{
    private readonly ILogger<EntraTokenProvider> _logger;
    private DefaultAzureCredential? _credential;

    public string Name => "Entra";

    // Azure DevOps application resource ID used for token acquisition.
    private const string DevOpsResource = "499b84ac-1321-427f-8633-9edcc3b9f0a9";

    public EntraTokenProvider(ILogger<EntraTokenProvider> logger)
    {
        _logger = logger;
    }

    public async ValueTask<string?> GetAuthorizationHeaderAsync(string organizationUrl, string? credentialKey, CancellationToken ct = default)
    {
        try
        {
            _credential ??= new DefaultAzureCredential();
            var scope = organizationUrl.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase)
                ? $"{organizationUrl}/.default"
                : $"{DevOpsResource}/.default";

            var token = await _credential.GetTokenAsync(new TokenRequestContext([scope]), ct).ConfigureAwait(false);
            return $"Bearer {token.Token}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to acquire Entra token for Azure DevOps");
            return null;
        }
    }

    public void Dispose()
    {
        (_credential as IDisposable)?.Dispose();
        _credential = null;
    }
}
