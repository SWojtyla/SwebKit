using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;

namespace SwebKit.Sidecar.Endpoints;

/// <summary>
/// Provides demo Service Bus namespaces and clients when demo mode is enabled.
/// Mirrors the old MAUI app's ServiceBusNamespaceBootstrapper.BuildDemoStates().
/// </summary>
public sealed class DemoModeService
{
    public static readonly Guid DemoNamespaceId1 = new("00000000-0000-0000-0000-000000000001");
    public static readonly Guid DemoNamespaceId2 = new("00000000-0000-0000-0000-000000000002");

    private readonly DemoServiceBusClient _ordersClient = DemoServiceBusClient.OrdersDev();
    private readonly DemoServiceBusClient _paymentsClient = DemoServiceBusClient.PaymentsDev();
    private readonly DemoAksClient _aksClient = new();

    public bool IsDemoMode { get; set; }

    public IReadOnlyList<ServiceBusNamespace> GetDemoNamespaces() =>
    [
        new ServiceBusNamespace
        {
            Id = DemoNamespaceId1,
            Alias = "orders-dev",
            FullyQualifiedNamespace = "orders-dev.servicebus.windows.net",
            CredentialKey = string.Empty,
        },
        new ServiceBusNamespace
        {
            Id = DemoNamespaceId2,
            Alias = "payments-dev",
            FullyQualifiedNamespace = "payments-dev.servicebus.windows.net",
            CredentialKey = string.Empty,
        },
    ];

    public IServiceBusClient GetSbClient(ServiceBusNamespace ns)
    {
        if (ns.Id == DemoNamespaceId1) return _ordersClient;
        if (ns.Id == DemoNamespaceId2) return _paymentsClient;
        throw new InvalidOperationException($"Unknown demo namespace: {ns.Alias}");
    }

    public IAksClient GetAksClient() => _aksClient;
}
