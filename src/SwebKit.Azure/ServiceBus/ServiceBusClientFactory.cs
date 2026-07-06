using Azure.Messaging.ServiceBus;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Services;

namespace SwebKit.Azure.ServiceBus;

/// <summary>
/// Creates <see cref="IServiceBusClient"/> instances backed by the Azure Service Bus SDK.
/// </summary>
public sealed class ServiceBusClientFactory : IServiceBusClientFactory
{
    public IServiceBusClient Create(string connectionString) =>
        new AzureServiceBusClient(connectionString);

    public IServiceBusClient CreateWithEntra(string fullyQualifiedNamespace) =>
        new AzureServiceBusClient(fullyQualifiedNamespace, AzureCredentialFactory.CreateDefault());

    public string ParseFullyQualifiedNamespace(string connectionString) =>
        ServiceBusConnectionStringProperties.Parse(connectionString).FullyQualifiedNamespace;
}
