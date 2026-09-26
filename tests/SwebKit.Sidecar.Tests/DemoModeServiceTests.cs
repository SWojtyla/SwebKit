using SwebKit.Sidecar.Services;
using System.Linq;
using SwebKit.Core.Domain;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

public class DemoModeServiceTests
{
    [Fact]
    public void GetDemoNamespaces_ReturnsBothDemoNamespaces_WithStableIds()
    {
        var service = new DemoModeService();

        var namespaces = service.GetDemoNamespaces();

        Assert.Equal(2, namespaces.Count);
        Assert.Contains(namespaces, n => n.Id == DemoModeService.DemoNamespaceId1 && n.Alias == "orders-dev");
        Assert.Contains(namespaces, n => n.Id == DemoModeService.DemoNamespaceId2 && n.Alias == "payments-dev");
    }

    [Fact]
    public void GetSbClient_OrdersNamespace_ReturnsSameInstanceEveryCall()
    {
        var service = new DemoModeService();
        var ns = service.GetDemoNamespaces().Single(n => n.Id == DemoModeService.DemoNamespaceId1);

        var first = service.GetSbClient(ns);
        var second = service.GetSbClient(ns);

        // The endpoints layer relies on getting back the same cached client instance across
        // requests, not a fresh one each time (demo data would otherwise reset every call).
        Assert.Same(first, second);
    }

    [Fact]
    public void GetSbClient_PaymentsNamespace_ReturnsADifferentClientThanOrders()
    {
        var service = new DemoModeService();
        var orders = service.GetDemoNamespaces().Single(n => n.Id == DemoModeService.DemoNamespaceId1);
        var payments = service.GetDemoNamespaces().Single(n => n.Id == DemoModeService.DemoNamespaceId2);

        var ordersClient = service.GetSbClient(orders);
        var paymentsClient = service.GetSbClient(payments);

        Assert.NotSame(ordersClient, paymentsClient);
    }

    [Fact]
    public void GetSbClient_UnknownNamespace_Throws()
    {
        var service = new DemoModeService();
        var unknown = new ServiceBusNamespace
        {
            Id = Guid.NewGuid(),
            Alias = "not-a-demo-namespace",
            FullyQualifiedNamespace = "whatever.servicebus.windows.net",
            CredentialKey = string.Empty,
        };

        var ex = Assert.Throws<InvalidOperationException>(() => service.GetSbClient(unknown));
        Assert.Contains("not-a-demo-namespace", ex.Message);
    }

    [Fact]
    public void GetDemoRedisCache_KnownId_ReturnsEntry()
    {
        var service = new DemoModeService();

        var cache = service.GetDemoRedisCache(DemoModeService.DemoRedisCacheId);

        Assert.NotNull(cache);
        Assert.Equal(DemoModeService.DemoRedisCacheId, cache!.Id);
    }

    [Fact]
    public void GetDemoRedisCache_UnknownId_ReturnsNull()
    {
        var service = new DemoModeService();

        Assert.Null(service.GetDemoRedisCache("not-the-demo-cache"));
    }

    [Fact]
    public void GetDemoStorageConfig_ReturnsAConfigThatAllowsMutations()
    {
        var service = new DemoModeService();

        var config = service.GetDemoStorageConfig();

        Assert.NotNull(config);
        Assert.Equal(DemoModeService.DemoStorageId, config!.Id);
        Assert.True(config.AllowMutations);
    }

    [Fact]
    public void IsDemoMode_DefaultsFalse_AndIsSettable()
    {
        var service = new DemoModeService();

        Assert.False(service.IsDemoMode);

        service.IsDemoMode = true;

        Assert.True(service.IsDemoMode);
    }

    [Fact]
    public async Task IsDemoMode_ReEnabled_ReseedsServiceBusData()
    {
        // Demo clients are stateful: mutations must not leak into the next demo session.
        // The Playwright suite toggles demo mode around every test and relies on each
        // test starting from the pristine seed data.
        var service = new DemoModeService { IsDemoMode = true };
        var ns = service.GetDemoNamespaces().Single(n => n.Id == DemoModeService.DemoNamespaceId1);
        var client = service.GetSbClient(ns);

        var seeded = await client.PeekMessagesAsync("order-created", 10);
        var seq = Assert.Single(seeded, m => m.MessageId == "oc-001").SequenceNumber!.Value;
        Assert.Equal(1, await client.CompleteMessagesAsync("order-created", [seq]));
        Assert.DoesNotContain(await client.PeekMessagesAsync("order-created", 10), m => m.MessageId == "oc-001");

        service.IsDemoMode = false;
        service.IsDemoMode = true;

        var reseeded = service.GetSbClient(ns);
        Assert.NotSame(client, reseeded);
        Assert.Contains(await reseeded.PeekMessagesAsync("order-created", 10), m => m.MessageId == "oc-001");
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var service = new DemoModeService();

        var ex = Record.Exception(service.Dispose);

        Assert.Null(ex);
    }
}
