using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using SwebKit.Sidecar.Endpoints;

namespace SwebKit.Sidecar.Tests;

/// <summary>
/// Exercises the extracted export/import handlers against a real <see cref="ConfigurationBundleService"/>
/// backed by real (file-persisting) repositories, sandboxed to a temp app-data root so the round trip
/// is genuine — including the actual JSON (de)serialization path — without touching the developer's
/// real %APPDATA%.
/// </summary>
/// <summary>No-op <see cref="IStorageConnectionPool"/> for tests that don't care about storage caching.</summary>
internal sealed class NoopStorageConnectionPool : IStorageConnectionPool
{
    public IStorageClient GetOrCreate(StorageConfig config) => throw new NotSupportedException();
    public void Evict(string accountId) { }
    public void InvalidateAll() { }
}

/// <summary>Records whether <see cref="InvalidateAll"/> was called, so a save's cache-busting can be asserted.</summary>
internal sealed class TrackingStorageConnectionPool : IStorageConnectionPool
{
    public int InvalidateAllCallCount { get; private set; }

    public IStorageClient GetOrCreate(StorageConfig config) => throw new NotSupportedException();
    public void Evict(string accountId) { }
    public void InvalidateAll() => InvalidateAllCallCount++;
}

/// <summary>Redis counterpart of <see cref="TrackingStorageConnectionPool"/>, also recording targeted evictions.</summary>
internal sealed class TrackingRedisConnectionPool : IRedisConnectionPool
{
    public int InvalidateAllCallCount { get; private set; }
    public List<string> EvictedIds { get; } = [];

    public ValueTask<IRedisClient> GetOrCreateAsync(RedisCacheEntry cache, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public void Evict(string cacheId) => EvictedIds.Add(cacheId);
    public void InvalidateAll() => InvalidateAllCallCount++;
}

/// <summary>Service Bus counterpart of <see cref="TrackingStorageConnectionPool"/>.</summary>
internal sealed class TrackingServiceBusConnectionPool : IServiceBusConnectionPool
{
    public int InvalidateAllCallCount { get; private set; }

    public IServiceBusClient GetOrCreate(ServiceBusNamespace ns) => throw new NotSupportedException();
    public void Evict(string namespaceId) { }
    public void InvalidateAll() => InvalidateAllCallCount++;
}

internal sealed class TrackingSqlConnectionPool : ISqlConnectionPool
{
    public int InvalidateAllCallCount { get; private set; }
    public List<string> EvictedIds { get; } = [];

    public ValueTask<ISqlClient> GetOrCreateAsync(SqlConnectionEntry connection, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public void Evict(string connectionId) => EvictedIds.Add(connectionId);
    public void InvalidateAll() => InvalidateAllCallCount++;
}

/// <summary>Monitoring-pool counterpart that records the targeted evictions a profile save issues.</summary>
internal sealed class TrackingMonitoringConnectionPool : IMonitoringConnectionPool
{
    public int EvictAksClientsCallCount { get; private set; }
    public List<string> EvictedServiceBusAliases { get; } = [];
    public List<string> EvictedRedisKeys { get; } = [];

    public IAksClient? GetAksClient() => throw new NotSupportedException();
    public IAksClient? GetAksClient(string? context) => throw new NotSupportedException();
    public IServiceBusClient? GetServiceBusClient(string alias) => throw new NotSupportedException();
    public ValueTask<IRedisClient?> GetRedisClientAsync(string displayName, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public void InvalidateStaleConnections() { }
    public void EvictServiceBusClient(string alias) => EvictedServiceBusAliases.Add(alias);
    public void EvictAksClients() => EvictAksClientsCallCount++;
    public void EvictRedisClient(string key) => EvictedRedisKeys.Add(key);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class ConfigEndpointsTests
{
    private static ConfigurationBundleService BuildService(out CollectionRepository collections, out ProfileRepository profiles)
    {
        profiles = new ProfileRepository();
        var uiState = new UiStateRepository();
        var userSettings = new UserSettingsRepository();
        var releases = new ReleaseRepository();
        var scheduledMessages = new ScheduledMessageRepository();
        var events = new AppEventBus(NullLogger<AppEventBus>.Instance);
        var appState = new AppStateService(profiles, uiState, events);
        collections = new CollectionRepository();
        var environments = new EnvironmentRepository();

        return new ConfigurationBundleService(profiles, uiState, userSettings, releases, scheduledMessages, appState, collections, environments);
    }

    private static DefaultHttpContext BuildImportHttpContext(string json)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        return ctx;
    }

    [Fact]
    public async Task ExportAsync_ReturnsJsonContentType_WithSerializedBundle()
    {
        using var sandbox = new AppDataSandbox();
        var svc = BuildService(out _, out var profiles);
        profiles.Config.Name = "export-test-profile";

        var result = ConfigEndpoints.ExportAsync(svc);

        Assert.Equal("application/json", result.ContentType);
        Assert.Contains("export-test-profile", result.ResponseContent);
        Assert.Contains("\"schemaVersion\"", result.ResponseContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAsync_RoundTrip_RestoresCollectionsIntoRepository()
    {
        using var sandbox = new AppDataSandbox();
        var exportSvc = BuildService(out var exportCollections, out _);
        await exportCollections.AddCollectionAsync("Imported Collection");
        var exportedJson = exportSvc.Serialize(exportSvc.Export());

        // Import into a fresh service/repository set (still sandboxed) to prove the round trip
        // actually persists via the extracted handler rather than relying on shared in-memory state.
        var importSvc = BuildService(out var importCollections, out _);
        var ctx = BuildImportHttpContext(exportedJson);

        var result = await ConfigEndpoints.ImportAsync(importSvc, ctx.Request);

        Assert.Equal(200, result.StatusCode);
        await importCollections.LoadAsync();
        Assert.Contains(importCollections.Collections, c => c.Name == "Imported Collection");
    }

    [Fact]
    public async Task ImportAsync_UnsupportedSchemaVersion_Throws_AndNeverPersists()
    {
        using var sandbox = new AppDataSandbox();
        var svc = BuildService(out var collections, out _);
        var badJson = """{"schemaVersion":99}""";
        var ctx = BuildImportHttpContext(badJson);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ConfigEndpoints.ImportAsync(svc, ctx.Request));

        // Nothing should have been written for an unsupported schema version.
        Assert.Empty(collections.Collections);
    }

    // ── Profiles ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetProfilesAsync_DemoMode_OverlaysDemoServiceBusRedisAndStorage()
    {
        var profile = new ProfileRepository();
        var demo = new DemoModeService { IsDemoMode = true };

        var result = Assert.IsAssignableFrom<IValueHttpResult>(ConfigEndpoints.GetProfilesAsync(profile, demo));
        var data = Assert.IsType<ProfileData>(result.Value);

        Assert.Equal(2, data.ServiceBusNamespaces.Count);
        Assert.NotNull(data.Config.RedisConfig);
        Assert.Single(data.Config.RedisConfig!.Caches);
        Assert.Single(data.Config.StorageAccounts);
    }

    [Fact]
    public void GetProfilesAsync_NonDemoMode_DoesNotMutateTheLiveRepository()
    {
        var profile = new ProfileRepository();
        var demo = new DemoModeService { IsDemoMode = false };

        var result = Assert.IsAssignableFrom<IValueHttpResult>(ConfigEndpoints.GetProfilesAsync(profile, demo));
        var data = Assert.IsType<ProfileData>(result.Value);
        data.ServiceBusNamespaces = [.. data.ServiceBusNamespaces, new ServiceBusNamespace
        {
            Id = Guid.NewGuid(),
            Alias = "mutated-in-response-clone",
            FullyQualifiedNamespace = "whatever.servicebus.windows.net",
            CredentialKey = string.Empty,
        }];

        // Mutating the returned clone must never leak back into the live repository.
        Assert.DoesNotContain(profile.ServiceBusNamespaces, n => n.Alias == "mutated-in-response-clone");
    }

    [Fact]
    public async Task SaveProfileAsync_PersistsProfileData()
    {
        using var sandbox = new AppDataSandbox();
        var profile = new ProfileRepository();
        var data = profile.GetProfileData();
        data.Config.Name = "saved-via-endpoint";

        await ConfigEndpoints.SaveProfileAsync(
            profile,
            data,
            new NoopStorageConnectionPool(),
            new TrackingRedisConnectionPool(),
            new TrackingServiceBusConnectionPool(),
            new TrackingSqlConnectionPool(),
            new TrackingMonitoringConnectionPool());

        var reloaded = new ProfileRepository();
        await reloaded.LoadAsync();
        Assert.Equal("saved-via-endpoint", reloaded.Config.Name);
    }

    [Fact]
    public async Task SaveProfileAsync_StripsDemoOverlayEntities()
    {
        // The profile GET overlays demo namespaces/cache/storage while demo mode is on, and saves
        // round-trip the whole profile — persisting them would make e.g. "demo-cache" resolve to a
        // real (dead localhost:6379) client once demo mode is off.
        using var sandbox = new AppDataSandbox();
        var profile = new ProfileRepository();
        var data = profile.GetProfileData();
        data.ServiceBusNamespaces =
        [
            new ServiceBusNamespace { Id = DemoModeService.DemoNamespaceId1, Alias = "orders-dev", CredentialKey = string.Empty },
            new ServiceBusNamespace { Id = DemoModeService.DemoNamespaceId2, Alias = "payments-dev", CredentialKey = string.Empty },
            new ServiceBusNamespace { Id = Guid.NewGuid(), Alias = "real-ns", CredentialKey = string.Empty },
        ];
        data.Config.RedisConfig = new RedisConfig
        {
            Caches =
            [
                new RedisCacheEntry { Id = DemoModeService.DemoRedisCacheId, ConnectionString = "localhost:6379" },
                new RedisCacheEntry { Id = "real-cache", ConnectionString = "real:6379" },
            ],
            ActiveCacheId = DemoModeService.DemoRedisCacheId,
        };
        data.Config.StorageAccounts =
        [
            new StorageConfig { Id = DemoModeService.DemoStorageId, DisplayName = "Demo Storage", AccountName = "devstore" },
            new StorageConfig { Id = "real-storage", DisplayName = "Real", AccountName = "real" },
        ];

        await ConfigEndpoints.SaveProfileAsync(
            profile,
            data,
            new NoopStorageConnectionPool(),
            new TrackingRedisConnectionPool(),
            new TrackingServiceBusConnectionPool(),
            new TrackingSqlConnectionPool(),
            new TrackingMonitoringConnectionPool());

        var stored = profile.GetProfileData();
        Assert.Single(stored.ServiceBusNamespaces);
        Assert.Equal("real-ns", stored.ServiceBusNamespaces[0].Alias);
        Assert.Single(stored.Config.RedisConfig!.Caches);
        Assert.Equal("real-cache", stored.Config.RedisConfig.Caches[0].Id);
        Assert.Equal("real-cache", stored.Config.RedisConfig.ActiveCacheId);
        Assert.Single(stored.Config.StorageAccounts);
        Assert.Equal("real-storage", stored.Config.StorageAccounts[0].Id);
    }

    [Fact]
    public async Task SaveProfileAsync_InvalidatesStorageAndServiceBus_EvictsOnlyStaleRedisCaches()
    {
        // A save may have edited a storage account's or Service Bus namespace's connection string,
        // credential key or auth mode — every cached client must be dropped so the next request
        // picks up the new config.
        //
        // Redis is the exception: it gets targeted eviction, because the Redis page persists its
        // selected cache on every switch and that PUT lands here — wholesale invalidation would
        // tear down every pooled Redis connection (and could dispose one an in-flight request is
        // using) each time the user just changes which cache they're looking at.
        using var sandbox = new AppDataSandbox();
        var profile = new ProfileRepository();
        profile.GetProfileData().Config.RedisConfig = new RedisConfig
        {
            Caches =
            [
                new RedisCacheEntry { Id = "unchanged", DisplayName = "cache-u", ConnectionString = "a:6379" },
                new RedisCacheEntry { Id = "edited", DisplayName = "cache-e", ConnectionString = "old:6379" },
                new RedisCacheEntry { Id = "removed", DisplayName = "cache-r", ConnectionString = "gone:6379" },
            ],
            ActiveCacheId = "unchanged",
        };
        // The PUT body arrives as a fresh object graph, so build one the same way — handing the
        // handler the live repository object would diff the cache list against itself.
        var data = new ProfileData();
        data.Config.RedisConfig = new RedisConfig
        {
            Caches =
            [
                new RedisCacheEntry { Id = "unchanged", ConnectionString = "a:6379" },
                new RedisCacheEntry { Id = "edited", ConnectionString = "new:6379" },
            ],
            // Selection-only change — must NOT evict anything.
            ActiveCacheId = "edited",
        };
        var storagePool = new TrackingStorageConnectionPool();
        var redisPool = new TrackingRedisConnectionPool();
        var serviceBusPool = new TrackingServiceBusConnectionPool();
        var sqlPool = new TrackingSqlConnectionPool();
        var monitoringPool = new TrackingMonitoringConnectionPool();

        await ConfigEndpoints.SaveProfileAsync(profile, data, storagePool, redisPool, serviceBusPool, sqlPool, monitoringPool);

        Assert.Equal(1, storagePool.InvalidateAllCallCount);
        Assert.Equal(1, serviceBusPool.InvalidateAllCallCount);
        Assert.Equal(0, redisPool.InvalidateAllCallCount);
        Assert.Equal(["edited", "removed"], redisPool.EvictedIds);
        // The monitoring pool gets the same targeted Redis eviction, keyed by both resolution
        // forms — and an ActiveCacheId-only save must not touch its AKS/Service Bus clients.
        Assert.Equal(["cache-e", "edited", "cache-r", "removed"], monitoringPool.EvictedRedisKeys);
        Assert.Equal(0, monitoringPool.EvictAksClientsCallCount);
        Assert.Empty(monitoringPool.EvictedServiceBusAliases);
    }

    [Fact]
    public async Task SaveProfileAsync_KubeconfigPathChange_EvictsAllPooledAksClients()
    {
        using var sandbox = new AppDataSandbox();
        var profile = new ProfileRepository();
        var sbId = Guid.NewGuid();
        profile.GetProfileData().Config.AksConfig = new AksConfig { KubeconfigPath = "/old/kubeconfig", KubeconfigContext = "ctx-a" };
        profile.GetProfileData().ServiceBusNamespaces.Add(new ServiceBusNamespace
        {
            Id = sbId,
            Alias = "orders",
            FullyQualifiedNamespace = "old.servicebus.windows.net",
            CredentialKey = "sb-key",
        });
        var data = new ProfileData();
        data.Config.AksConfig = new AksConfig { KubeconfigPath = "/new/kubeconfig", KubeconfigContext = "ctx-a" };
        data.ServiceBusNamespaces =
        [
            new ServiceBusNamespace
            {
                Id = sbId,
                Alias = "orders",
                FullyQualifiedNamespace = "new.servicebus.windows.net",
                CredentialKey = "sb-key",
            },
        ];
        var monitoringPool = new TrackingMonitoringConnectionPool();

        await ConfigEndpoints.SaveProfileAsync(
            profile,
            data,
            new NoopStorageConnectionPool(),
            new TrackingRedisConnectionPool(),
            new TrackingServiceBusConnectionPool(),
            new TrackingSqlConnectionPool(),
            monitoringPool);

        // A kubeconfig path change invalidates every pooled AKS client (they were all built from
        // the old file); the Service Bus FQDN change evicts that namespace's client under both
        // resolution keys (alias and id).
        Assert.Equal(1, monitoringPool.EvictAksClientsCallCount);
        Assert.Equal(["orders", sbId.ToString("N")], monitoringPool.EvictedServiceBusAliases);
    }

    [Fact]
    public async Task SaveProfileAsync_ContextOnlyChange_KeepsPooledAksClientsWarm()
    {
        // The context selection is the pool's cache key — changing it leaves every pooled client
        // valid, so evicting here would just destroy the namespace cache the switch-back relies on.
        using var sandbox = new AppDataSandbox();
        var profile = new ProfileRepository();
        profile.GetProfileData().Config.AksConfig = new AksConfig { KubeconfigPath = "/same/kubeconfig", KubeconfigContext = "ctx-a" };
        var data = new ProfileData();
        data.Config.AksConfig = new AksConfig { KubeconfigPath = "/same/kubeconfig", KubeconfigContext = "ctx-b" };
        var monitoringPool = new TrackingMonitoringConnectionPool();

        await ConfigEndpoints.SaveProfileAsync(
            profile,
            data,
            new NoopStorageConnectionPool(),
            new TrackingRedisConnectionPool(),
            new TrackingServiceBusConnectionPool(),
            new TrackingSqlConnectionPool(),
            monitoringPool);

        Assert.Equal(0, monitoringPool.EvictAksClientsCallCount);
    }

    [Fact]
    public void StaleServiceBusNamespaces_OnlyFlagsRemovedOrConnectionChangedEntries()
    {
        var idSame = Guid.NewGuid();
        var idRenamed = Guid.NewGuid();
        var idCred = Guid.NewGuid();
        var idTransport = Guid.NewGuid();
        var idRemoved = Guid.NewGuid();
        var before = new List<ServiceBusNamespace>
        {
            new() { Id = idSame, Alias = "same", CredentialKey = "k1" },
            new() { Id = idRenamed, Alias = "old-alias", CredentialKey = "k2" },
            new() { Id = idCred, Alias = "cred", CredentialKey = "k3" },
            new() { Id = idTransport, Alias = "transport", CredentialKey = "k4", TransportType = SbTransportType.Amqp },
            new() { Id = idRemoved, Alias = "gone", CredentialKey = "k5" },
        };
        var after = new List<ServiceBusNamespace>
        {
            new() { Id = idSame, Alias = "same", CredentialKey = "k1" },
            // An alias rename alone doesn't affect the pooled connection — not stale.
            new() { Id = idRenamed, Alias = "new-alias", CredentialKey = "k2" },
            new() { Id = idCred, Alias = "cred", CredentialKey = "k3-new" },
            new() { Id = idTransport, Alias = "transport", CredentialKey = "k4", TransportType = SbTransportType.AmqpWebSockets },
        };

        Assert.Equal(
            [idCred, idTransport, idRemoved],
            ConfigEndpoints.StaleServiceBusNamespaces(before, after).Select(n => n.Id).ToList());
    }

    [Fact]
    public void StaleSqlConnectionIds_OnlyFlagsRemovedOrConnectionChangedEntries()
    {
        var before = new List<SqlConnectionEntry>
        {
            new() { Id = "same", Server = "a.database.windows.net", Database = "orders" },
            new() { Id = "renamed", Server = "b.database.windows.net", Database = "orders", DisplayName = "Old" },
            new() { Id = "server-changed", Server = "old.database.windows.net", Database = "orders" },
            new() { Id = "database-changed", Server = "a.database.windows.net", Database = "old" },
            new() { Id = "removed", Server = "gone.database.windows.net", Database = "orders" },
        };
        var after = new List<SqlConnectionEntry>
        {
            new() { Id = "same", Server = "A.DATABASE.WINDOWS.NET", Database = "ORDERS", AllowWrites = true },
            new() { Id = "renamed", Server = "b.database.windows.net", Database = "orders", DisplayName = "New" },
            new() { Id = "server-changed", Server = "new.database.windows.net", Database = "orders" },
            new() { Id = "database-changed", Server = "a.database.windows.net", Database = "new" },
            new() { Id = "added", Server = "new.database.windows.net", Database = "orders" },
        };

        Assert.Equal(
            ["server-changed", "database-changed", "removed"],
            ConfigEndpoints.StaleSqlConnectionIds(before, after).ToList());
    }

    [Fact]
    public void StaleRedisCacheIds_OnlyFlagsRemovedOrConnectionChangedCaches()
    {
        var before = new List<RedisCacheEntry>
        {
            new() { Id = "same", ConnectionString = "a:6379" },
            new() { Id = "renamed", ConnectionString = "b:6379", DisplayName = "Old name" },
            new() { Id = "connstring-changed", ConnectionString = "old:6379" },
            new() { Id = "aad-changed", UseAad = true, CacheName = "old-cache" },
            new() { Id = "removed", ConnectionString = "x:6379" },
        };
        var after = new List<RedisCacheEntry>
        {
            new() { Id = "same", ConnectionString = "a:6379" },
            // DisplayName doesn't affect the connection — not stale.
            new() { Id = "renamed", ConnectionString = "b:6379", DisplayName = "New name" },
            new() { Id = "connstring-changed", ConnectionString = "new:6379" },
            new() { Id = "aad-changed", UseAad = true, CacheName = "new-cache" },
            // Newly added — nothing pooled for it yet, so nothing to evict.
            new() { Id = "added", ConnectionString = "new:6380" },
        };

        Assert.Equal(
            ["connstring-changed", "aad-changed", "removed"],
            ConfigEndpoints.StaleRedisCacheIds(before, after).ToList());
    }

    // ── Environments ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveEnvironmentsAsync_ThenGetEnvironments_RoundTrips()
    {
        using var sandbox = new AppDataSandbox();
        var repo = new EnvironmentRepository();
        var store = new EnvironmentsStore
        {
            Environments = [new ApiEnvironment { Id = "env-1", Name = "Staging", Variables = [] }],
        };

        await ConfigEndpoints.SaveEnvironmentsAsync(repo, store);
        var result = Assert.IsAssignableFrom<IValueHttpResult>(ConfigEndpoints.GetEnvironments(repo));
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);

        Assert.Contains("Staging", json);
    }

    // ── Collections ──────────────────────────────────────────────────────────

    [Fact]
    public void GetCollections_DemoMode_PrependsTheDemoCollectionFirst()
    {
        var repo = new CollectionRepository();
        var demo = new DemoModeService { IsDemoMode = true };

        var result = Assert.IsAssignableFrom<IValueHttpResult>(ConfigEndpoints.GetCollections(repo, demo));
        var collections = Assert.IsAssignableFrom<IReadOnlyList<ApiCollection>>(result.Value);

        Assert.Equal(DemoApiCollectionFactory.DemoCollectionId, collections[0].Id);
    }

    [Fact]
    public void GetCollections_NonDemoMode_DoesNotIncludeTheDemoCollection()
    {
        var repo = new CollectionRepository();
        var demo = new DemoModeService { IsDemoMode = false };

        var result = Assert.IsAssignableFrom<IValueHttpResult>(ConfigEndpoints.GetCollections(repo, demo));
        var collections = Assert.IsAssignableFrom<IReadOnlyList<ApiCollection>>(result.Value);

        Assert.DoesNotContain(collections, c => c.Id == DemoApiCollectionFactory.DemoCollectionId);
    }

    [Fact]
    public void GetCollectionsStore_DemoMode_PrependsTheDemoCollectionFirst()
    {
        var repo = new CollectionRepository();
        var demo = new DemoModeService { IsDemoMode = true };

        var result = Assert.IsAssignableFrom<IValueHttpResult>(ConfigEndpoints.GetCollectionsStore(repo, demo));
        var store = Assert.IsType<CollectionsStoreResponse>(result.Value);

        Assert.Equal(DemoApiCollectionFactory.DemoCollectionId, store.Collections[0].Id);
        Assert.Equal(1, store.SchemaVersion);
    }

    // ── User settings ────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveUserSettingsAsync_ThenGetUserSettings_RoundTrips()
    {
        using var sandbox = new AppDataSandbox();
        var repo = new UserSettingsRepository();
        var settings = repo.Settings;
        settings.Logging.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Debug;

        await ConfigEndpoints.SaveUserSettingsAsync(repo, settings);
        var result = Assert.IsAssignableFrom<IValueHttpResult>(ConfigEndpoints.GetUserSettings(repo));
        var reloaded = Assert.IsType<UserSettings>(result.Value);

        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Debug, reloaded.Logging.MinimumLevel);
    }
}
