using Azure.Messaging.ServiceBus;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Azure.ServiceBus;

/// <summary>
/// Creates <see cref="IServiceBusClient"/> instances backed by the Azure Service Bus SDK.
/// </summary>
public sealed class ServiceBusClientFactory : IServiceBusClientFactory
{
    public IServiceBusClient Create(string connectionString, SbTransportType transportType = SbTransportType.Amqp) =>
        new AzureServiceBusClient(connectionString, BuildOptions(transportType));

    public IServiceBusClient CreateWithEntra(string fullyQualifiedNamespace, SbTransportType transportType = SbTransportType.Amqp) =>
        new AzureServiceBusClient(fullyQualifiedNamespace, AzureCredentialFactory.CreateDefault(), BuildOptions(transportType));

    public string ParseFullyQualifiedNamespace(string connectionString) =>
        ServiceBusConnectionStringProperties.Parse(connectionString).FullyQualifiedNamespace;

    /// <inheritdoc />
    public ServiceBusConnectionDiagnostic BuildConnectionDiagnostic(string connectionString, string credentialSource)
    {
        // SECURITY (DEC-3): read ONLY non-secret identifiers from the parsed properties.
        // Never read props.SharedAccessKey (the secret value) and never retain the raw connection string.
        var props = ServiceBusConnectionStringProperties.Parse(connectionString);
        var keyName = string.IsNullOrWhiteSpace(props.SharedAccessKeyName) ? null : props.SharedAccessKeyName;

        return new ServiceBusConnectionDiagnostic(
            EndpointHost: props.FullyQualifiedNamespace,
            SharedAccessKeyName: keyName,
            AuthMethod: "SAS key",
            CredentialSource: string.IsNullOrWhiteSpace(credentialSource) ? "(unnamed credential)" : credentialSource);
    }

    /// <inheritdoc />
    public ServiceBusConnectionDiagnostic BuildEntraConnectionDiagnostic(string fullyQualifiedNamespace) =>
        new(EndpointHost: fullyQualifiedNamespace,
            SharedAccessKeyName: null,
            AuthMethod: "Microsoft Entra (DefaultAzureCredential)",
            CredentialSource: "DefaultAzureCredential");

    private static ServiceBusClientOptions BuildOptions(SbTransportType transportType) =>
        new() { TransportType = MapTransportType(transportType) };

    /// <summary>Maps the domain <see cref="SbTransportType"/> to the SDK's <see cref="ServiceBusTransportType"/>.</summary>
    internal static ServiceBusTransportType MapTransportType(SbTransportType transportType) => transportType switch
    {
        SbTransportType.AmqpWebSockets => ServiceBusTransportType.AmqpWebSockets,
        _ => ServiceBusTransportType.AmqpTcp
    };
}
