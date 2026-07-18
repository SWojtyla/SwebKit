using Azure;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Azure.ServiceBus;

/// <summary>
/// Centralizes creation of Service Bus client and diagnostic objects from connection strings, Entra credentials,
/// or legacy config. Ensures non-secret diagnostics are built before any network call and that connection strings
/// are parsed once.
/// </summary>
internal static class ServiceBusClientConnectionFactory
{
    /// <summary>
    /// Creates a data-plane <see cref="ServiceBusClient"/> from a connection string with optional transport override.
    /// </summary>
    public static ServiceBusClient CreateClient(string connectionString, ServiceBusTransportType transportType)
    {
        var options = new ServiceBusClientOptions { TransportType = transportType };
        return new ServiceBusClient(connectionString, options);
    }

    /// <summary>
    /// Creates a data-plane <see cref="ServiceBusClient"/> authenticated via Microsoft Entra ID.
    /// </summary>
    public static ServiceBusClient CreateClient(string fullyQualifiedNamespace, TokenCredential credential, ServiceBusTransportType transportType)
    {
        var options = new ServiceBusClientOptions { TransportType = transportType };
        return new ServiceBusClient(fullyQualifiedNamespace, credential, options);
    }

    /// <summary>
    /// Creates an administration client from a connection string.
    /// </summary>
    public static ServiceBusAdministrationClient CreateAdminClient(string connectionString) =>
        new(connectionString);

    /// <summary>
    /// Creates an administration client authenticated via Microsoft Entra ID.
    /// </summary>
    public static ServiceBusAdministrationClient CreateAdminClient(string fullyQualifiedNamespace, TokenCredential credential) =>
        new(fullyQualifiedNamespace, credential);

    /// <summary>
    /// Builds non-secret diagnostics from a raw connection string.
    /// SECURITY: only the endpoint host and SAS key name are read — never the key value or raw string.
    /// </summary>
    public static ServiceBusConnectionDiagnostic BuildConnectionDiagnostic(string connectionString, string credentialSource)
    {
        var props = ServiceBusConnectionStringProperties.Parse(connectionString);
        var keyName = string.IsNullOrWhiteSpace(props.SharedAccessKeyName) ? null : props.SharedAccessKeyName;

        return new ServiceBusConnectionDiagnostic(
            EndpointHost: props.FullyQualifiedNamespace,
            SharedAccessKeyName: keyName,
            AuthMethod: "SAS key",
            CredentialSource: string.IsNullOrWhiteSpace(credentialSource) ? "(unnamed credential)" : credentialSource);
    }

    /// <summary>
    /// Builds non-secret diagnostics for Microsoft Entra authentication.
    /// </summary>
    public static ServiceBusConnectionDiagnostic BuildEntraConnectionDiagnostic(string fullyQualifiedNamespace) =>
        new(EndpointHost: fullyQualifiedNamespace,
            SharedAccessKeyName: null,
            AuthMethod: "Microsoft Entra (DefaultAzureCredential)",
            CredentialSource: "DefaultAzureCredential");

    /// <summary>
    /// Parses the connection string to extract the optional scoped entity path, or returns null.
    /// </summary>
    public static string? GetScopedEntityPath(string connectionString)
    {
        var props = ServiceBusConnectionStringProperties.Parse(connectionString);
        return string.IsNullOrWhiteSpace(props.EntityPath) ? null : props.EntityPath;
    }

    /// <summary>
    /// Creates client and admin client from a legacy <see cref="ServiceBusConfig"/>.
    /// </summary>
    public static (ServiceBusClient Client, ServiceBusAdministrationClient AdminClient, ServiceBusConnectionDiagnostic? Diagnostic) CreateFromConfig(
        ServiceBusConfig config,
        ICredentialStore credentialStore)
    {
        var fqns = config.FullyQualifiedNamespace;
        var transportType = ServiceBusClientFactory.MapTransportType(config.TransportType);

        if (config.AuthMode == SbAuthMode.ConnectionString && config.CredentialRef is not null)
        {
            var connStr = credentialStore.Get(config.CredentialRef)
                ?? throw new InvalidOperationException($"Credential '{config.CredentialRef}' not found.");

            var diagnostic = BuildConnectionDiagnostic(connStr, config.CredentialRef);
            var client = CreateClient(connStr, transportType);
            var adminClient = CreateAdminClient(connStr);
            return (client, adminClient, diagnostic);
        }

        var credential = AzureCredentialFactory.CreateDefault();
        var entraClient = CreateClient(fqns, credential, transportType);
        var entraAdminClient = CreateAdminClient(fqns, credential);
        return (entraClient, entraAdminClient, BuildEntraConnectionDiagnostic(fqns));
    }
}
