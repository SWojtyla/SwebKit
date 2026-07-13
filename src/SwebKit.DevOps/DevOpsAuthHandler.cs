using System.Net.Http.Headers;
using System.Text;
using SwebKit.Core.Abstractions;

namespace SwebKit.DevOps;

/// <summary>
/// DelegatingHandler that injects Azure DevOps PAT as a Basic auth header.
/// Reads the PAT from ICredentialStore using the key provided on the current request.
/// The PAT value is never logged or exposed in error messages.
/// </summary>
public class DevOpsAuthHandler : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<string> PatCredentialKeyOption = new("SwebKit.DevOps.PatCredentialKey");

    private readonly ICredentialStore _credentialStore;

    public DevOpsAuthHandler(ICredentialStore credentialStore)
    {
        _credentialStore = credentialStore;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(PatCredentialKeyOption, out var patCredentialKey)
            && !string.IsNullOrWhiteSpace(patCredentialKey))
        {
            var pat = _credentialStore.Get(patCredentialKey);
            if (!string.IsNullOrEmpty(pat))
            {
                var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
