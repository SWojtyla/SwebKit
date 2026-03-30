using System.Net.Http.Headers;
using System.Text;
using SwebKit.Core.Abstractions;

namespace SwebKit.DevOps;

/// <summary>
/// DelegatingHandler that injects Azure DevOps PAT as a Basic auth header.
/// Reads the PAT from ICredentialStore using the key set via <see cref="SetCredentialKey"/>.
/// The PAT value is never logged or exposed in error messages.
/// </summary>
public class DevOpsAuthHandler : DelegatingHandler
{
    private readonly ICredentialStore _credentialStore;
    private volatile string? _patCredentialKey;

    public DevOpsAuthHandler(ICredentialStore credentialStore)
    {
        _credentialStore = credentialStore;
    }

    public void SetCredentialKey(string patCredentialKey)
    {
        _patCredentialKey = patCredentialKey;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_patCredentialKey))
        {
            var pat = _credentialStore.Get(_patCredentialKey);
            if (!string.IsNullOrEmpty(pat))
            {
                var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
