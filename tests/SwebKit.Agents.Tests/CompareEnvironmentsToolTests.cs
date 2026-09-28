using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SwebKit.Agents.Tools.Compare;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Security;
using SwebKit.Core.Services;
using Xunit;

namespace SwebKit.Agents.Tests;

/// <summary>
/// <see cref="CompareEnvironmentsTool"/> — logical-name lookup, the per-side outcome
/// vocabulary (ok / missing / access_denied / error), and the demo map pair the tool
/// substitutes while demo mode is on. The AKS/Service Bus/Redis fakes are Moq; SQL
/// reuses <see cref="FakeSqlClientFactoryForTools"/>; the access report reuses
/// <see cref="FakeAccessReportService"/>.
/// </summary>
public class CompareEnvironmentsToolTests
{
    private static JsonElement Args(string logicalName, string envA, string envB) =>
        JsonSerializer.SerializeToDocument(
            new { logical_name = logicalName, env_a = envA, env_b = envB }).RootElement;

    private sealed record Harness(
        AppStateService State,
        ProfileRepository Profiles,
        Mock<IAksClientFactory> AksFactory,
        Mock<IServiceBusConnectionPool> SbPool,
        FakeSqlClientFactoryForTools SqlFactory,
        Mock<IRedisClientFactory> RedisFactory,
        FakeAccessReportService Report,
        CompareEnvironmentsTool Tool);

    /// <summary>One shared <see cref="ProfileRepository"/> backs both the app state and the
    /// tool — the tool reads maps/SQL/Redis off it, the Service Bus namespaces off appState.</summary>
    private static Harness Build(
        Action<AppConfig>? configure = null,
        IReadOnlyList<ServiceBusNamespace>? serviceBusNamespaces = null)
    {
        var config = new AppConfig { Name = "Test" };
        configure?.Invoke(config);
        var profiles = new ProfileRepository();
        profiles.ReplaceProfileData(new ProfileData
        {
            Config = config,
            ServiceBusNamespaces = serviceBusNamespaces is null ? [] : [.. serviceBusNamespaces],
        });
        var state = new AppStateService(
            profiles, new UiStateRepository(), new AppEventBus(NullLogger<AppEventBus>.Instance));

        var aksFactory = new Mock<IAksClientFactory>();
        var sbPool = new Mock<IServiceBusConnectionPool>();
        var sqlFactory = new FakeSqlClientFactoryForTools();
        var redisFactory = new Mock<IRedisClientFactory>();
        var report = new FakeAccessReportService();
        var tool = new CompareEnvironmentsTool(
            profiles, state, aksFactory.Object, new DemoAksClient(),
            sbPool.Object, sqlFactory, redisFactory.Object, report);
        return new(state, profiles, aksFactory, sbPool, sqlFactory, redisFactory, report, tool);
    }

    private static WorkspaceMap Map(string id, string name, params WorkspaceResourceNode[] nodes) =>
        new() { Id = id, Name = name, Nodes = [.. nodes] };

    private static WorkspaceResourceNode Node(
        WorkspaceResourceArea area, string key, string? logicalName = null, string? context = null) =>
        new() { Area = area, ResourceKey = key, DisplayLabel = key, LogicalName = logicalName, KubeconfigContext = context };

    /// <summary>Duck-typed 403 — <see cref="AccessAdvisor"/> classifies by type name +
    /// StatusCode, so a plain HttpRequestException carrying Forbidden is a denial.</summary>
    private static readonly HttpRequestException Forbidden =
        new("forbidden", null, HttpStatusCode.Forbidden);

    private static JsonElement Comparison(JsonElement root, string area) =>
        root.GetProperty("comparisons").EnumerateArray()
            .Single(c => c.GetProperty("area").GetString() == area);

    // ── logical-name / env lookup ───────────────────────────────────────────

    [Fact]
    public void FindLogicalNodes_CaseInsensitiveAndTrims()
    {
        var map = Map("m", "dev",
            Node(WorkspaceResourceArea.Sql, "s1/db", logicalName: "Order-Api"),
            Node(WorkspaceResourceArea.Sql, "s2/db", logicalName: "other"),
            Node(WorkspaceResourceArea.Aks, "ns/api", logicalName: " order-api "));

        var found = CompareToolContext.FindLogicalNodes(map, "ORDER-API");

        Assert.Equal(2, found.Count);
    }

    [Fact]
    public void FindLogicalNodes_NoLogicalNames_ReturnsEmpty()
    {
        var map = Map("m", "dev", Node(WorkspaceResourceArea.Redis, "cache"));
        Assert.Empty(CompareToolContext.FindLogicalNodes(map, "anything"));
    }

    [Fact]
    public void ResolveMap_MatchesNameAndId_CaseInsensitive()
    {
        var maps = new[] { Map("map-1", "orders-dev"), Map("map-2", "orders-prd") };
        Assert.Same(maps[0], CompareToolContext.ResolveMap(maps, "ORDERS-DEV"));
        Assert.Same(maps[1], CompareToolContext.ResolveMap(maps, "map-2"));
        Assert.Null(CompareToolContext.ResolveMap(maps, "nope"));
    }

    // ── argument / resolution errors ────────────────────────────────────────

    [Fact]
    public async Task UnknownEnv_ReportsErrorWithMapHint()
    {
        var h = Build(c => c.Maps.Add(Map("m1", "dev")));
        var result = await h.Tool.ExecuteAsync(Args("order-api", "dev", "staging"), CancellationToken.None);
        using var doc = JsonDocument.Parse(result);
        Assert.Contains("staging", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("dev", doc.RootElement.GetProperty("hints").GetProperty("available_maps")[0].GetString());
    }

    [Fact]
    public async Task SameMapTwice_ReportsError()
    {
        var h = Build(c => c.Maps.Add(Map("m1", "dev")));
        var result = await h.Tool.ExecuteAsync(Args("x", "dev", "dev"), CancellationToken.None);
        Assert.Contains("same map", result);
    }

    [Fact]
    public async Task UnknownLogicalName_ListsNamesOnBothMaps()
    {
        var h = Build(c =>
        {
            c.Maps.Add(Map("m1", "dev", Node(WorkspaceResourceArea.Redis, "r1", "session-cache")));
            c.Maps.Add(Map("m2", "prd", Node(WorkspaceResourceArea.Redis, "r2", "cart-cache")));
        });
        var result = await h.Tool.ExecuteAsync(Args("nope", "dev", "prd"), CancellationToken.None);
        using var doc = JsonDocument.Parse(result);
        var hints = doc.RootElement.GetProperty("hints");
        Assert.Equal("session-cache", hints.GetProperty("logical_names_in_env_a")[0].GetString());
        Assert.Equal("cart-cache", hints.GetProperty("logical_names_in_env_b")[0].GetString());
    }

    // ── both sides OK ───────────────────────────────────────────────────────

    [Fact]
    public async Task Aks_BothSidesOk_DiffsDeploymentFields()
    {
        var deploymentsA = new[]
        {
            new DeploymentInfo { Name = "order-api", Namespace = "ecommerce", Replicas = 3, ReadyReplicas = 3, Status = "Available", ImageTag = "3.14.2" },
        };
        var deploymentsB = new[]
        {
            new DeploymentInfo { Name = "order-api", Namespace = "ecommerce", Replicas = 5, ReadyReplicas = 5, Status = "Available", ImageTag = "3.15.0" },
        };
        var h = Build(c =>
        {
            c.AksConfig = new AksConfig { KubeconfigContext = "ctx-dev" };
            c.Maps.Add(Map("m1", "dev", Node(WorkspaceResourceArea.Aks, "ecommerce/order-api", "order-api")));
            c.Maps.Add(Map("m2", "prd", Node(WorkspaceResourceArea.Aks, "ecommerce/order-api", "order-api", context: "ctx-prd")));
        });
        var clientA = new Mock<IAksClient>();
        clientA.Setup(x => x.GetDeploymentsAsync("ecommerce", It.IsAny<CancellationToken>())).ReturnsAsync(deploymentsA);
        var clientB = new Mock<IAksClient>();
        clientB.Setup(x => x.GetDeploymentsAsync("ecommerce", It.IsAny<CancellationToken>())).ReturnsAsync(deploymentsB);
        // The unpinned dev node falls back to the configured context; the prd node is pinned.
        h.AksFactory.Setup(f => f.Create("ctx-dev", null)).Returns(clientA.Object);
        h.AksFactory.Setup(f => f.Create("ctx-prd", null)).Returns(clientB.Object);

        var result = await h.Tool.ExecuteAsync(Args("order-api", "dev", "prd"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        var cmp = Comparison(doc.RootElement, "aks");
        Assert.Equal("different", cmp.GetProperty("status").GetString());
        Assert.Equal("ok", cmp.GetProperty("env_a").GetProperty("status").GetString());
        Assert.Equal("ok", cmp.GetProperty("env_b").GetProperty("status").GetString());
        var fields = cmp.GetProperty("differences").EnumerateArray()
            .Select(d => d.GetProperty("field").GetString()).ToList();
        Assert.Contains("image_tag", fields);
        Assert.Contains("replicas", fields);
        // The node pinned to ctx-prd queried that cluster, not the configured one.
        h.AksFactory.Verify(f => f.Create("ctx-prd", null), Times.Once);
    }

    [Fact]
    public async Task ServiceBus_BothSidesOk_DiffsQueueFlagsAndStats()
    {
        var nsA = new ServiceBusNamespace { Id = Guid.NewGuid(), Alias = "dev", FullyQualifiedNamespace = "sb-dev.servicebus.windows.net" };
        var nsB = new ServiceBusNamespace { Id = Guid.NewGuid(), Alias = "prd", FullyQualifiedNamespace = "sb-prd.servicebus.windows.net" };
        var h = Build(
            c =>
            {
                c.Maps.Add(Map("m1", "dev", Node(WorkspaceResourceArea.ServiceBus, "sb-dev.servicebus.windows.net/orders", "orders-queue")));
                c.Maps.Add(Map("m2", "prd", Node(WorkspaceResourceArea.ServiceBus, "sb-prd.servicebus.windows.net/orders", "orders-queue")));
            },
            serviceBusNamespaces: [nsA, nsB]);
        var clientA = new Mock<IServiceBusClient>();
        clientA.Setup(x => x.ListQueuesAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<SbEntityInfo>)
        [
            new SbEntityInfo
            {
                Name = "orders", EntityPath = "orders", IsDisabled = false, RequiresSession = false,
                Stats = new SbEntityStats { ActiveMessageCount = 12, DeadLetterMessageCount = 1 },
            },
        ]);
        var clientB = new Mock<IServiceBusClient>();
        clientB.Setup(x => x.ListQueuesAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<SbEntityInfo>)
        [
            new SbEntityInfo
            {
                Name = "orders", EntityPath = "orders", IsDisabled = true, RequiresSession = false,
                Stats = new SbEntityStats { ActiveMessageCount = 0, DeadLetterMessageCount = 40 },
            },
        ]);
        h.SbPool.Setup(p => p.GetOrCreate(It.Is<ServiceBusNamespace>(n => n == nsA))).Returns(clientA.Object);
        h.SbPool.Setup(p => p.GetOrCreate(It.Is<ServiceBusNamespace>(n => n == nsB))).Returns(clientB.Object);

        var result = await h.Tool.ExecuteAsync(Args("orders-queue", "dev", "prd"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        var cmp = Comparison(doc.RootElement, "servicebus");
        Assert.Equal("different", cmp.GetProperty("status").GetString());
        var diffs = cmp.GetProperty("differences").EnumerateArray().ToList();
        var disabled = diffs.Single(d => d.GetProperty("field").GetString() == "is_disabled");
        Assert.False(disabled.GetProperty("env_a").GetBoolean());
        Assert.True(disabled.GetProperty("env_b").GetBoolean());
    }

    [Fact]
    public async Task Sql_BothSidesOk_ReportsSchemaDrift()
    {
        var h = Build(c =>
        {
            c.SqlConfig = new SqlConfig
            {
                Connections =
                [
                    new SqlConnectionEntry { Id = "sq-dev", Server = "dev-sql.database.windows.net", Database = "orders" },
                    new SqlConnectionEntry { Id = "sq-prd", Server = "prd-sql.database.windows.net", Database = "orders" },
                ],
            };
            c.Maps.Add(Map("m1", "dev", Node(WorkspaceResourceArea.Sql, "dev-sql.database.windows.net/orders", "orders-db")));
            c.Maps.Add(Map("m2", "prd", Node(WorkspaceResourceArea.Sql, "prd-sql.database.windows.net/orders", "orders-db")));
        });
        // Both demo schemas are identical (one canned catalog) → equal.
        var result = await h.Tool.ExecuteAsync(Args("orders-db", "dev", "prd"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        var cmp = Comparison(doc.RootElement, "sql");
        Assert.Equal("equal", cmp.GetProperty("status").GetString());
        Assert.Equal("ok", cmp.GetProperty("env_a").GetProperty("status").GetString());
        Assert.Equal("ok", cmp.GetProperty("env_b").GetProperty("status").GetString());
        Assert.Equal(2, h.SqlFactory.Calls.Count);
    }

    [Fact]
    public async Task Redis_BothSidesOk_DiffsVersionAndKeyCounts()
    {
        var h = Build(c =>
        {
            c.RedisConfig = new RedisConfig
            {
                Caches =
                [
                    new RedisCacheEntry { Id = "cache-dev", DisplayName = "dev cache" },
                    new RedisCacheEntry { Id = "cache-prd", DisplayName = "prd cache" },
                ],
            };
            c.Maps.Add(Map("m1", "dev", Node(WorkspaceResourceArea.Redis, "cache-dev", "session-cache")));
            c.Maps.Add(Map("m2", "prd", Node(WorkspaceResourceArea.Redis, "cache-prd", "session-cache")));
        });
        var clientA = new Mock<IRedisClient>();
        clientA.Setup(x => x.GetServerInfoAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RedisServerInfo
        {
            RedisVersion = "7.2.1", MaxMemoryBytes = 1000,
            Databases = [new RedisDatabaseInfo { Index = 0, Keys = 100 }],
        });
        var clientB = new Mock<IRedisClient>();
        clientB.Setup(x => x.GetServerInfoAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RedisServerInfo
        {
            RedisVersion = "7.4.0", MaxMemoryBytes = 2000,
            Databases = [new RedisDatabaseInfo { Index = 0, Keys = 980 }],
        });
        h.RedisFactory.Setup(f => f.CreateAsync(
                It.Is<RedisCacheEntry>(e => e.Id == "cache-dev"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientA.Object);
        h.RedisFactory.Setup(f => f.CreateAsync(
                It.Is<RedisCacheEntry>(e => e.Id == "cache-prd"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientB.Object);

        var result = await h.Tool.ExecuteAsync(Args("session-cache", "dev", "prd"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        var cmp = Comparison(doc.RootElement, "redis");
        Assert.Equal("different", cmp.GetProperty("status").GetString());
        var fields = cmp.GetProperty("differences").EnumerateArray()
            .Select(d => d.GetProperty("field").GetString()).ToList();
        Assert.Contains("redis_version", fields);
        Assert.Contains("max_memory_bytes", fields);
        Assert.Contains("db0.keys", fields);
    }

    // ── missing side ────────────────────────────────────────────────────────

    [Fact]
    public async Task NodeOnlyOnOneMap_OtherSideIsMissing_NotAbsent()
    {
        var h = Build(c =>
        {
            c.Maps.Add(Map("m1", "dev", Node(WorkspaceResourceArea.Redis, "cache-dev", "session-cache")));
            c.Maps.Add(Map("m2", "prd", Node(WorkspaceResourceArea.Sql, "s/db", "session-cache")));
            c.RedisConfig = new RedisConfig { Caches = [new RedisCacheEntry { Id = "cache-dev" }] };
            c.SqlConfig = new SqlConfig { Connections = [new SqlConnectionEntry { Id = "sq-prd", Server = "s", Database = "db" }] };
        });
        var client = new Mock<IRedisClient>();
        client.Setup(x => x.GetServerInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RedisServerInfo { RedisVersion = "7.2.1" });
        h.RedisFactory.Setup(f => f.CreateAsync(It.IsAny<RedisCacheEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);
        h.SqlFactory.Client = new FakeSqlClientForTools();

        var result = await h.Tool.ExecuteAsync(Args("session-cache", "dev", "prd"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        var comparisons = doc.RootElement.GetProperty("comparisons").EnumerateArray().ToList();
        Assert.Equal(2, comparisons.Count); // redis + sql — one entry per area present on either map

        var redis = comparisons.Single(c => c.GetProperty("area").GetString() == "redis");
        Assert.Equal("incomplete", redis.GetProperty("status").GetString());
        Assert.Equal("ok", redis.GetProperty("env_a").GetProperty("status").GetString());
        Assert.Equal("missing", redis.GetProperty("env_b").GetProperty("status").GetString());

        var sql = comparisons.Single(c => c.GetProperty("area").GetString() == "sql");
        Assert.Equal("missing", sql.GetProperty("env_a").GetProperty("status").GetString());
        Assert.Equal("ok", sql.GetProperty("env_b").GetProperty("status").GetString());
    }

    [Fact]
    public async Task ResourceMissingInsideArea_SideReportsMissing()
    {
        // Both maps name a SQL server, but env B's server isn't a configured connection.
        var h = Build(c =>
        {
            c.SqlConfig = new SqlConfig
            {
                Connections = [new SqlConnectionEntry { Id = "sq-dev", Server = "dev-sql.database.windows.net", Database = "orders" }],
            };
            c.Maps.Add(Map("m1", "dev", Node(WorkspaceResourceArea.Sql, "dev-sql.database.windows.net/orders", "orders-db")));
            c.Maps.Add(Map("m2", "prd", Node(WorkspaceResourceArea.Sql, "unknown-sql.database.windows.net/orders", "orders-db")));
        });
        var result = await h.Tool.ExecuteAsync(Args("orders-db", "dev", "prd"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        var cmp = Comparison(doc.RootElement, "sql");
        Assert.Equal("incomplete", cmp.GetProperty("status").GetString());
        Assert.Equal("ok", cmp.GetProperty("env_a").GetProperty("status").GetString());
        Assert.Equal("missing", cmp.GetProperty("env_b").GetProperty("status").GetString());
        Assert.Contains("unknown-sql", cmp.GetProperty("env_b").GetProperty("message").GetString());
    }

    // ── denied side ─────────────────────────────────────────────────────────

    [Fact]
    public async Task KnownDenial_SideIsAccessDenied_AndClientNeverCalled()
    {
        var nsDenied = new ServiceBusNamespace { Id = Guid.NewGuid(), Alias = "prd", FullyQualifiedNamespace = "sb-prd.servicebus.windows.net" };
        var nsOk = new ServiceBusNamespace { Id = Guid.NewGuid(), Alias = "dev", FullyQualifiedNamespace = "sb-dev.servicebus.windows.net" };
        var h = Build(
            c =>
            {
                c.Maps.Add(Map("m1", "dev", Node(WorkspaceResourceArea.ServiceBus, "sb-dev.servicebus.windows.net/orders", "orders-queue")));
                c.Maps.Add(Map("m2", "prd", Node(WorkspaceResourceArea.ServiceBus, "sb-prd.servicebus.windows.net/orders", "orders-queue")));
            },
            serviceBusNamespaces: [nsDenied, nsOk]);
        h.Report.Seed("ServiceBus", nsDenied.Id.ToString(), new AccessDenial(
            "ServiceBus", "servicebus.manage", "Azure Service Bus Data Receiver",
            "Ask a resource owner for the role.", "probed 403"));
        var clientA = new Mock<IServiceBusClient>();
        clientA.Setup(x => x.ListQueuesAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<SbEntityInfo>)
        [
            new SbEntityInfo { Name = "orders", EntityPath = "orders", Stats = new SbEntityStats { ActiveMessageCount = 3 } },
        ]);
        h.SbPool.Setup(p => p.GetOrCreate(It.Is<ServiceBusNamespace>(n => n == nsOk))).Returns(clientA.Object);

        var result = await h.Tool.ExecuteAsync(Args("orders-queue", "dev", "prd"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        var cmp = Comparison(doc.RootElement, "servicebus");
        Assert.Equal("incomplete", cmp.GetProperty("status").GetString());
        Assert.Equal("ok", cmp.GetProperty("env_a").GetProperty("status").GetString());
        var sideB = cmp.GetProperty("env_b");
        Assert.Equal("access_denied", sideB.GetProperty("status").GetString());
        Assert.True(sideB.GetProperty("cached").GetBoolean()); // short-circuited, not re-denied live
        Assert.Equal("servicebus.manage", sideB.GetProperty("denial").GetProperty("capability").GetString());
        // The denied namespace's client was never built.
        h.SbPool.Verify(p => p.GetOrCreate(It.Is<ServiceBusNamespace>(n => n == nsDenied)), Times.Never);
    }

    [Fact]
    public async Task ThrownDenial_SideIsAccessDenied_AndFeedsTheAccessReport()
    {
        var nsOk = new ServiceBusNamespace { Id = Guid.NewGuid(), Alias = "dev", FullyQualifiedNamespace = "sb-dev.servicebus.windows.net" };
        var nsDenied = new ServiceBusNamespace { Id = Guid.NewGuid(), Alias = "prd", FullyQualifiedNamespace = "sb-prd.servicebus.windows.net" };
        var h = Build(
            c =>
            {
                c.Maps.Add(Map("m1", "dev", Node(WorkspaceResourceArea.ServiceBus, "sb-dev.servicebus.windows.net/orders", "orders-queue")));
                c.Maps.Add(Map("m2", "prd", Node(WorkspaceResourceArea.ServiceBus, "sb-prd.servicebus.windows.net/orders", "orders-queue")));
            },
            serviceBusNamespaces: [nsOk, nsDenied]);
        var clientA = new Mock<IServiceBusClient>();
        clientA.Setup(x => x.ListQueuesAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<SbEntityInfo>)
        [
            new SbEntityInfo { Name = "orders", EntityPath = "orders", Stats = new SbEntityStats { ActiveMessageCount = 3 } },
        ]);
        var clientB = new Mock<IServiceBusClient>();
        clientB.Setup(x => x.ListQueuesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(Forbidden);
        h.SbPool.Setup(p => p.GetOrCreate(It.Is<ServiceBusNamespace>(n => n == nsOk))).Returns(clientA.Object);
        h.SbPool.Setup(p => p.GetOrCreate(It.Is<ServiceBusNamespace>(n => n == nsDenied))).Returns(clientB.Object);

        var result = await h.Tool.ExecuteAsync(Args("orders-queue", "dev", "prd"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        var cmp = Comparison(doc.RootElement, "servicebus");
        Assert.Equal("ok", cmp.GetProperty("env_a").GetProperty("status").GetString());
        var sideB = cmp.GetProperty("env_b");
        Assert.Equal("access_denied", sideB.GetProperty("status").GetString());
        Assert.False(sideB.GetProperty("cached").GetBoolean()); // denied live, not short-circuited
        Assert.Equal("servicebus.manage", sideB.GetProperty("denial").GetProperty("capability").GetString());

        // Observed denials feed the report keyed on the denied namespace — the
        // coarse area capability is refined to servicebus.manage, mirroring the registry.
        var (denial, connectionKey) = Assert.Single(h.Report.Recorded);
        Assert.Equal(nsDenied.Id.ToString(), connectionKey);
        Assert.Equal("servicebus.manage", denial.Capability);
        Assert.Equal("ServiceBus", denial.FeatureArea);
    }

    [Fact]
    public async Task Sql_MetadataHidden_SideIsAccessDenied()
    {
        // The demo restricted connection (SELECT/EXECUTE, no VIEW DEFINITION) lands on the same
        // access_denied outcome a live SqlException 229 would.
        var h = Build();
        await h.State.SetDemoModeAsync(true);
        try
        {
            var result = await h.Tool.ExecuteAsync(Args("orders-db", "orders-dev", "orders-prd"), CancellationToken.None);
            using var doc = JsonDocument.Parse(result);
            var cmp = Comparison(doc.RootElement, "sql");
            Assert.Equal("incomplete", cmp.GetProperty("status").GetString());
            Assert.Equal("ok", cmp.GetProperty("env_a").GetProperty("status").GetString());
            var sideB = cmp.GetProperty("env_b");
            Assert.Equal("access_denied", sideB.GetProperty("status").GetString());
            Assert.Equal("sql.metadata", sideB.GetProperty("denial").GetProperty("capability").GetString());
            Assert.Equal("VIEW DEFINITION", sideB.GetProperty("denial").GetProperty("required_access").GetString());
        }
        finally
        {
            await h.State.SetDemoModeAsync(false);
        }
    }

    // ── error side ──────────────────────────────────────────────────────────

    [Fact]
    public async Task NonDenialException_SideIsError_AndCompareContinues()
    {
        var h = Build(c =>
        {
            c.RedisConfig = new RedisConfig { Caches = [new RedisCacheEntry { Id = "cache-dev" }] };
            c.Maps.Add(Map("m1", "dev", Node(WorkspaceResourceArea.Redis, "cache-dev", "session-cache")));
            c.Maps.Add(Map("m2", "prd", Node(WorkspaceResourceArea.Redis, "cache-dev", "session-cache")));
        });
        // Two nodes, one configured cache: dev resolves, prd would resolve to the same cache —
        // make the factory throw a non-authz error for the second call to prove the side lands
        // on "error" rather than sinking the compare.
        var client = new Mock<IRedisClient>();
        client.SetupSequence(x => x.GetServerInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RedisServerInfo { RedisVersion = "7.2.1" })
            .ThrowsAsync(new InvalidOperationException("connection reset"));
        h.RedisFactory.Setup(f => f.CreateAsync(It.IsAny<RedisCacheEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client.Object);

        var result = await h.Tool.ExecuteAsync(Args("session-cache", "dev", "prd"), CancellationToken.None);

        using var doc = JsonDocument.Parse(result);
        var cmp = Comparison(doc.RootElement, "redis");
        Assert.Equal("incomplete", cmp.GetProperty("status").GetString());
        Assert.Equal("ok", cmp.GetProperty("env_a").GetProperty("status").GetString());
        Assert.Equal("error", cmp.GetProperty("env_b").GetProperty("status").GetString());
        Assert.Contains("connection reset", cmp.GetProperty("env_b").GetProperty("message").GetString());
    }

    // ── demo maps ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DemoMode_SeededMaps_CompareAllOutcomes()
    {
        var h = Build(); // no configured maps — demo substitutes the synthetic pair
        await h.State.SetDemoModeAsync(true);
        try
        {
            var result = await h.Tool.ExecuteAsync(Args("order-api", "orders-dev", "orders-prd"), CancellationToken.None);
            using var doc = JsonDocument.Parse(result);
            var cmp = Comparison(doc.RootElement, "aks");
            Assert.Equal("ok", cmp.GetProperty("env_a").GetProperty("status").GetString());
            Assert.Equal("ok", cmp.GetProperty("env_b").GetProperty("status").GetString());
            Assert.Equal("equal", cmp.GetProperty("status").GetString()); // one canned catalog serves all demo contexts
        }
        finally
        {
            await h.State.SetDemoModeAsync(false);
        }
    }

    [Fact]
    public async Task DemoMode_ServiceBus_DifferentStats()
    {
        var h = Build();
        await h.State.SetDemoModeAsync(true);
        try
        {
            var result = await h.Tool.ExecuteAsync(Args("order-events", "orders-dev", "orders-prd"), CancellationToken.None);
            using var doc = JsonDocument.Parse(result);
            var cmp = Comparison(doc.RootElement, "servicebus");
            Assert.Equal("different", cmp.GetProperty("status").GetString());
        }
        finally
        {
            await h.State.SetDemoModeAsync(false);
        }
    }

    [Fact]
    public async Task DemoMode_Redis_MissingPrdSide()
    {
        var h = Build();
        await h.State.SetDemoModeAsync(true);
        try
        {
            var result = await h.Tool.ExecuteAsync(Args("session-cache", "orders-dev", "orders-prd"), CancellationToken.None);
            using var doc = JsonDocument.Parse(result);
            var cmp = Comparison(doc.RootElement, "redis");
            Assert.Equal("ok", cmp.GetProperty("env_a").GetProperty("status").GetString());
            Assert.Equal("missing", cmp.GetProperty("env_b").GetProperty("status").GetString());
            Assert.Equal("incomplete", cmp.GetProperty("status").GetString());
        }
        finally
        {
            await h.State.SetDemoModeAsync(false);
        }
    }
}
