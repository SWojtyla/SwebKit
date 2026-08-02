using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SwebKit.Agents.Tools.Redis;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Models;
using SwebKit.Core.Services;
using Xunit;

namespace SwebKit.Agents.Tests;

public class RedisToolsTests
{
    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    private static (AppStateService AppState, ProfileRepository Profiles) CreateContext(
        List<RedisCacheEntry>? caches = null, string? activeCacheId = null)
    {
        var config = new AppConfig { Name = "Test" };
        if (caches is not null)
            config.RedisConfig = new RedisConfig { Caches = caches, ActiveCacheId = activeCacheId };

        var repo = new ProfileRepository();
        repo.ReplaceProfileData(new ProfileData { Config = config });
        var appState = new AppStateService(repo, new UiStateRepository(), new AppEventBus(NullLogger<AppEventBus>.Instance));
        return (appState, repo);
    }

    private static RedisCacheEntry Cache(string id = "c1", string name = "Prod Cache") => new()
    {
        Id = id,
        DisplayName = name,
        ConnectionString = "localhost:6379",
        Database = 0,
    };

    private static Mock<IRedisClientFactory> MakeFactory(IRedisClient client)
    {
        var factory = new Mock<IRedisClientFactory>();
        factory.Setup(f => f.CreateAsync(It.IsAny<RedisCacheEntry>(), It.IsAny<CancellationToken>())).ReturnsAsync(client);
        return factory;
    }

    // ── GetRedisKeyInfoTool ──────────────────────────────────────────────────

    [Fact]
    public async Task GetKeyInfo_NoCacheConfigured_ReturnsError()
    {
        var (appState, profiles) = CreateContext();
        var tool = new GetRedisKeyInfoTool(appState, profiles, Mock.Of<IRedisClientFactory>());

        var result = await tool.ExecuteAsync(Args("""{"key":"session:1"}"""), CancellationToken.None);

        Assert.Contains("not configured", JsonDocument.Parse(result).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetKeyInfo_MissingKey_ReturnsError_WithoutTouchingTheClient()
    {
        var client = new Mock<IRedisClient>();
        var (appState, profiles) = CreateContext(caches: [Cache()]);
        var tool = new GetRedisKeyInfoTool(appState, profiles, MakeFactory(client.Object).Object);

        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        Assert.Contains("Missing required parameter", JsonDocument.Parse(result).RootElement.GetProperty("error").GetString());
        client.Verify(c => c.GetKeyInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetKeyInfo_ExistingKey_ReturnsTypeTtlAndMemory()
    {
        var client = new Mock<IRedisClient>();
        client.Setup(c => c.GetKeyInfoAsync("session:1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RedisKeyInfo { Key = "session:1", Type = "hash", Ttl = TimeSpan.FromSeconds(300), MemoryBytes = 128 });
        var (appState, profiles) = CreateContext(caches: [Cache()]);
        var tool = new GetRedisKeyInfoTool(appState, profiles, MakeFactory(client.Object).Object);

        var result = await tool.ExecuteAsync(Args("""{"key":"session:1"}"""), CancellationToken.None);

        var root = JsonDocument.Parse(result).RootElement;
        Assert.Equal("hash", root.GetProperty("type").GetString());
        Assert.Equal(300, root.GetProperty("ttl_seconds").GetDouble());
        Assert.Equal(128, root.GetProperty("memory_bytes").GetInt64());
    }

    [Fact]
    public async Task GetKeyInfo_NonexistentKey_ReturnsError()
    {
        var client = new Mock<IRedisClient>();
        client.Setup(c => c.GetKeyInfoAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RedisKeyInfo { Key = "missing", Type = "none" });
        var (appState, profiles) = CreateContext(caches: [Cache()]);
        var tool = new GetRedisKeyInfoTool(appState, profiles, MakeFactory(client.Object).Object);

        var result = await tool.ExecuteAsync(Args("""{"key":"missing"}"""), CancellationToken.None);

        Assert.Contains("does not exist", JsonDocument.Parse(result).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetKeyInfo_RequestedCacheIdNotFound_ReturnsError()
    {
        var (appState, profiles) = CreateContext(caches: [Cache("c1")]);
        var tool = new GetRedisKeyInfoTool(appState, profiles, Mock.Of<IRedisClientFactory>());

        var result = await tool.ExecuteAsync(Args("""{"key":"k","cache_id":"does-not-exist"}"""), CancellationToken.None);

        Assert.Contains("not found", JsonDocument.Parse(result).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetKeyInfo_DemoMode_UsesDemoClient_WithoutRequiringAnyConfiguredCache()
    {
        var (appState, profiles) = CreateContext();
        await appState.SetDemoModeAsync(true);
        var tool = new GetRedisKeyInfoTool(appState, profiles, Mock.Of<IRedisClientFactory>());

        var result = await tool.ExecuteAsync(Args("""{"key":"anything"}"""), CancellationToken.None);

        // Demo client is real (DemoRedisClient), not mocked — just assert it didn't hit the
        // "not configured" error path, proving the demo branch was taken.
        using var doc = JsonDocument.Parse(result);
        Assert.False(doc.RootElement.TryGetProperty("error", out var err) && err.GetString()!.Contains("not configured"));
    }

    // ── ListRedisKeysTool ────────────────────────────────────────────────────

    [Fact]
    public async Task ListKeys_ReturnsKeysFromScan_DefaultingPatternToStar()
    {
        var client = new Mock<IRedisClient>();
        client.Setup(c => c.ScanKeysAsync("*", 0, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KeyScanResult { Cursor = 0, Keys = ["a", "b"], IsComplete = true });
        var (appState, profiles) = CreateContext(caches: [Cache()]);
        var tool = new ListRedisKeysTool(appState, profiles, MakeFactory(client.Object).Object);

        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        var root = JsonDocument.Parse(result).RootElement;
        Assert.Equal(2, root.GetProperty("key_count").GetInt32());
        Assert.False(root.GetProperty("more_available").GetBoolean());
    }

    // ── AnalyzeCacheHealthTool ───────────────────────────────────────────────

    [Theory]
    [InlineData(50, 100, 0, "Healthy")]   // low memory %, no slow entries
    [InlineData(80, 100, 0, "Warning")]   // >75% memory
    [InlineData(50, 100, 2, "Warning")]   // some slow entries, memory fine
    [InlineData(95, 100, 0, "Critical")]  // >90% memory
    [InlineData(10, 100, 6, "Critical")]  // many slow entries regardless of memory
    public async Task AnalyzeCacheHealth_ComputesHealthSummary(long usedMemory, long maxMemory, int slowLogCount, string expected)
    {
        var client = new Mock<IRedisClient>();
        client.Setup(c => c.GetServerInfoAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RedisServerInfo
        {
            RedisVersion = "7.0",
            UsedMemoryBytes = usedMemory,
            MaxMemoryBytes = maxMemory,
        });
        client.Setup(c => c.GetSlowLogAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(
            new RedisSlowLogSummary(
                Enumerable.Range(0, slowLogCount)
                    .Select(i => new RedisSlowLogEntryInfo(i, DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(50), "GET", "k", null))
                    .ToList(),
                Truncated: false,
                MaxReturned: 10,
                Capability: RedisInsightCapability.Loaded));

        var (appState, profiles) = CreateContext(caches: [Cache()]);
        var tool = new AnalyzeCacheHealthTool(appState, profiles, MakeFactory(client.Object).Object);

        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        Assert.Equal(expected, JsonDocument.Parse(result).RootElement.GetProperty("health_summary").GetString());
    }

    [Fact]
    public async Task AnalyzeCacheHealth_ClientThrows_ReturnsCriticalWithError()
    {
        var client = new Mock<IRedisClient>();
        client.Setup(c => c.GetServerInfoAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        client.Setup(c => c.GetSlowLogAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RedisSlowLogSummary([], false, 10, RedisInsightCapability.Loaded));
        var (appState, profiles) = CreateContext(caches: [Cache()]);
        var tool = new AnalyzeCacheHealthTool(appState, profiles, MakeFactory(client.Object).Object);

        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        var root = JsonDocument.Parse(result).RootElement;
        Assert.Equal("Critical", root.GetProperty("health_summary").GetString());
        Assert.Equal("boom", root.GetProperty("error").GetString());
    }

    // ── ProposeDeleteRedisKeyTool / ProposeSetRedisKeyTtlTool ───────────────

    [Fact]
    public async Task ProposeDeleteKey_RegistersHighRiskAction_WithPayload()
    {
        var coordinator = new AgentActionCoordinator();
        var (appState, profiles) = CreateContext(caches: [Cache()]);
        var tool = new ProposeDeleteRedisKeyTool(appState, profiles, coordinator);

        var result = await tool.ExecuteAsync(Args("""{"key":"session:1"}"""), CancellationToken.None);

        var actionId = JsonDocument.Parse(result).RootElement.GetProperty("action_id").GetString()!;
        var action = coordinator.GetAction(actionId);
        Assert.NotNull(action);
        Assert.Equal(AgentActionType.DeleteRedisKey, action!.Type);
        Assert.Equal(AgentActionRisk.High, action.Risk);
        Assert.Equal("session:1", action.Payload!.Value.GetProperty("key").GetString());
    }

    [Fact]
    public async Task ProposeSetTtl_RegistersLowRiskAction()
    {
        var coordinator = new AgentActionCoordinator();
        var (appState, profiles) = CreateContext(caches: [Cache()]);
        var tool = new ProposeSetRedisKeyTtlTool(appState, profiles, coordinator);

        var result = await tool.ExecuteAsync(Args("""{"key":"session:1","ttl_seconds":60}"""), CancellationToken.None);

        var actionId = JsonDocument.Parse(result).RootElement.GetProperty("action_id").GetString()!;
        var action = coordinator.GetAction(actionId);
        Assert.NotNull(action);
        Assert.Equal(AgentActionType.SetRedisKeyTtl, action!.Type);
        Assert.Equal(AgentActionRisk.Low, action.Risk);
    }
}
