using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Azure.ServiceBus;

namespace SwebKit.App.Services;

public sealed class ServiceBusNamespaceBootstrapper : IServiceBusNamespaceBootstrapper
{
    private static readonly Guid DemoNamespaceId1 = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DemoNamespaceId2 = new("00000000-0000-0000-0000-000000000002");

    private readonly ICredentialStore _credentialStore;

    public ServiceBusNamespaceBootstrapper(ICredentialStore credentialStore)
    {
        _credentialStore = credentialStore;
    }

    public IReadOnlyList<ServiceBusNamespaceBootstrapState> BuildInitialStates(
        IReadOnlyList<ServiceBusNamespace> configuredNamespaces,
        IReadOnlyDictionary<Guid, ServiceBusNamespaceBootstrapSnapshot> cachedSnapshots,
        bool useDemoData)
    {
        var states = configuredNamespaces.Select(ns =>
        {
            if (cachedSnapshots.TryGetValue(ns.Id, out var snapshot))
            {
                return new ServiceBusNamespaceBootstrapState(
                    Namespace: ns,
                    Client: null,
                    ShouldConnect: snapshot.WasConnected,
                    ConnectionError: snapshot.WasConnected ? null : snapshot.Error,
                    IsDemo: false);
            }

            return new ServiceBusNamespaceBootstrapState(
                Namespace: ns,
                Client: null,
                ShouldConnect: true,
                ConnectionError: null,
                IsDemo: false);
        }).ToList();

        if (!useDemoData || states.Any(state => state.Namespace.Id == DemoNamespaceId1))
        {
            return states;
        }

        states.Add(new ServiceBusNamespaceBootstrapState(
            Namespace: new ServiceBusNamespace
            {
                Id = DemoNamespaceId1,
                Alias = "orders-dev",
                FullyQualifiedNamespace = "orders-dev.servicebus.windows.net",
                CredentialKey = string.Empty
            },
            Client: DemoServiceBusClient.OrdersDev(),
            ShouldConnect: false,
            ConnectionError: null,
            IsDemo: true));

        states.Add(new ServiceBusNamespaceBootstrapState(
            Namespace: new ServiceBusNamespace
            {
                Id = DemoNamespaceId2,
                Alias = "payments-dev",
                FullyQualifiedNamespace = "payments-dev.servicebus.windows.net",
                CredentialKey = string.Empty
            },
            Client: DemoServiceBusClient.PaymentsDev(),
            ShouldConnect: false,
            ConnectionError: null,
            IsDemo: true));

        return states;
    }

    public async Task<ServiceBusNamespaceConnectionResult> ConnectAsync(ServiceBusNamespace ns, CancellationToken ct = default)
    {
        AzureServiceBusClient? client = null;

        try
        {
            var connectionString = _credentialStore.Get(ns.CredentialKey);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return new ServiceBusNamespaceConnectionResult(
                    Client: null,
                    ConnectionError: "Connection string not found in credential store.");
            }

            client = new AzureServiceBusClient(connectionString);
            var ok = await client.TestConnectionAsync(ct);
            if (!ok)
            {
                await client.DisposeAsync();
                return new ServiceBusNamespaceConnectionResult(
                    Client: null,
                    ConnectionError: "Connection test failed. Check the connection string.");
            }

            return new ServiceBusNamespaceConnectionResult(client, ConnectionError: null);
        }
        catch (OperationCanceledException)
        {
            if (client is not null)
            {
                await client.DisposeAsync();
            }

            throw;
        }
        catch (Exception ex)
        {
            if (client is not null)
            {
                await client.DisposeAsync();
            }

            return new ServiceBusNamespaceConnectionResult(Client: null, ConnectionError: ex.Message);
        }
    }
}