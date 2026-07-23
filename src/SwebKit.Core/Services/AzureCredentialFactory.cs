using Azure.Core;
using Azure.Identity;

namespace SwebKit.Core.Services;

/// <summary>
/// Single source of truth for constructing the <see cref="TokenCredential"/> used by every
/// Entra ID (Azure AD) authenticated client in the app (Storage, Service Bus, Key Vault,
/// Application Insights, etc.).
/// </summary>
/// <remarks>
/// <see cref="DefaultAzureCredential"/> tries credential sources in a fixed order, and
/// <c>EnvironmentCredential</c> is tried before <c>AzureCliCredential</c>/<c>VisualStudioCredential</c>.
/// On a dev machine that has <c>AZURE_CLIENT_ID</c>/<c>AZURE_TENANT_ID</c>/<c>AZURE_CLIENT_SECRET</c>
/// set system-wide for an unrelated service principal (e.g. a local automation tool), that SP
/// would silently win over the signed-in developer's own identity and RBAC grants — with no
/// visible indication of which identity is actually being used. Excluding
/// <see cref="EnvironmentCredential"/> here ensures the app always falls through to the
/// developer's interactive credential instead. See docs/pitfalls/azure-sdk.md (AZ-4).
/// </remarks>
public static class AzureCredentialFactory
{
    /// <summary>Creates the app-wide default Entra ID credential.</summary>
    public static TokenCredential CreateDefault() =>
        CreateDefault(new DefaultAzureCredentialOptions
        {
            ExcludeEnvironmentCredential = true,
            // This is a desktop app running on developer machines, never on Azure VMs.
            // ManagedIdentityCredential probes the IMDS endpoint (169.254.169.254) which
            // doesn't exist locally, causing a 6-retry socket timeout before falling through
            // to the next credential. Excluding it eliminates the delay and the confusing error.
            ExcludeManagedIdentityCredential = true
        });

    /// <summary>
    /// Creates an Entra ID credential with the specified options.
    /// Use this overload when a caller needs different exclusion settings
    /// (e.g., KubernetesAksClient excludes WorkloadIdentityCredential).
    /// </summary>
    public static TokenCredential CreateDefault(DefaultAzureCredentialOptions options) =>
        new DefaultAzureCredential(options);
}
