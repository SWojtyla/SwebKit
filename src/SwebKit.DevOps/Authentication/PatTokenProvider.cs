using System.Net.Http.Headers;
using System.Text;
using SwebKit.Core.Abstractions;

namespace SwebKit.DevOps.Authentication;

/// <summary>PAT-based token provider for Azure DevOps Basic authentication.</summary>
public sealed class PatTokenProvider : IAuthenticationTokenProvider
{
    private readonly ICredentialStore _credentialStore;

    public string Name => "Pat";

    public PatTokenProvider(ICredentialStore credentialStore)
    {
        _credentialStore = credentialStore;
    }

    public ValueTask<string?> GetAuthorizationHeaderAsync(string organizationUrl, string? credentialKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(credentialKey))
            return ValueTask.FromResult<string?>(null);

        var pat = _credentialStore.Get(credentialKey);
        if (string.IsNullOrWhiteSpace(pat))
            return ValueTask.FromResult<string?>(null);

        // ADO Basic auth requires :{PAT} as the password with an empty username.
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        return ValueTask.FromResult<string?>($"Basic {token}");
    }
}
