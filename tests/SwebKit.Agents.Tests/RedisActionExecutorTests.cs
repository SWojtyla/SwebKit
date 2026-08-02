using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SwebKit.Agents.Tools.Redis;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using Xunit;

namespace SwebKit.Agents.Tests;

public class RedisActionExecutorTests
{
    private static (AppStateService AppState, ProfileRepository Profiles) CreateContext(RedisCacheEntry cache)
    {
        var config = new AppConfig { Name = "Test", RedisConfig = new RedisConfig { Caches = [cache] } };
        var repo = new ProfileRepository();
        repo.ReplaceProfileData(new ProfileData { Config = config });
        return (new AppStateService(repo, new UiStateRepository(), new AppEventBus(NullLogger<AppEventBus>.Instance)), repo);
    }

    private static PendingAgentAction ActionFor(AgentActionType type, object payload) => new()
    {
        Id = "a1",
        Type = type,
        Summary = "S",
        Target = "T",
        Risk = AgentActionRisk.High,
        Preview = "P",
        ExpectedFingerprint = null,
        Payload = JsonSerializer.SerializeToElement(payload),
    };

    [Fact]
    public void CanHandle_OnlyRedisActionTypes()
    {
        var executor = new RedisActionExecutor(
            new AppStateService(new ProfileRepository(), new UiStateRepository(), new AppEventBus(NullLogger<AppEventBus>.Instance)),
            new ProfileRepository(),
            Mock.Of<IRedisClientFactory>());

        Assert.True(executor.CanHandle(AgentActionType.DeleteRedisKey));
        Assert.True(executor.CanHandle(AgentActionType.SetRedisKeyTtl));
        Assert.False(executor.CanHandle(AgentActionType.DeleteRequest));
    }

    [Fact]
    public async Task ApplyAsync_Delete_CallsDeleteKeysAsync_WithExactlyTheProposedKey()
    {
        var client = new Mock<IRedisClient>();
        var factory = new Mock<IRedisClientFactory>();
        var cache = new RedisCacheEntry { Id = "c1", ConnectionString = "x" };
        factory.Setup(f => f.CreateAsync(cache, It.IsAny<CancellationToken>())).ReturnsAsync(client.Object);
        var (appState, profiles) = CreateContext(cache);
        var executor = new RedisActionExecutor(appState, profiles, factory.Object);

        var result = await executor.ApplyAsync(ActionFor(AgentActionType.DeleteRedisKey, new { key = "session:1" }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        client.Verify(c => c.DeleteKeysAsync(
            It.Is<IReadOnlyList<string>>(keys => keys.Count == 1 && keys[0] == "session:1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_SetTtl_WithSeconds_CallsSetTtlAsync()
    {
        var client = new Mock<IRedisClient>();
        var factory = new Mock<IRedisClientFactory>();
        var cache = new RedisCacheEntry { Id = "c1", ConnectionString = "x" };
        factory.Setup(f => f.CreateAsync(cache, It.IsAny<CancellationToken>())).ReturnsAsync(client.Object);
        var (appState, profiles) = CreateContext(cache);
        var executor = new RedisActionExecutor(appState, profiles, factory.Object);

        var result = await executor.ApplyAsync(
            ActionFor(AgentActionType.SetRedisKeyTtl, new { key = "session:1", ttl_seconds = 60 }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        client.Verify(c => c.SetTtlAsync("session:1", TimeSpan.FromSeconds(60), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_SetTtl_WithoutSeconds_CallsRemoveTtlAsync()
    {
        var client = new Mock<IRedisClient>();
        var factory = new Mock<IRedisClientFactory>();
        var cache = new RedisCacheEntry { Id = "c1", ConnectionString = "x" };
        factory.Setup(f => f.CreateAsync(cache, It.IsAny<CancellationToken>())).ReturnsAsync(client.Object);
        var (appState, profiles) = CreateContext(cache);
        var executor = new RedisActionExecutor(appState, profiles, factory.Object);

        var result = await executor.ApplyAsync(ActionFor(AgentActionType.SetRedisKeyTtl, new { key = "session:1" }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        client.Verify(c => c.RemoveTtlAsync("session:1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_MissingPayload_FailsWithoutResolvingAnyClient()
    {
        var factory = new Mock<IRedisClientFactory>();
        var cache = new RedisCacheEntry { Id = "c1", ConnectionString = "x" };
        var (appState, profiles) = CreateContext(cache);
        var executor = new RedisActionExecutor(appState, profiles, factory.Object);
        var action = new PendingAgentAction
        {
            Id = "a1", Type = AgentActionType.DeleteRedisKey, Summary = "S", Target = "T",
            Risk = AgentActionRisk.High, Preview = "P", ExpectedFingerprint = null, Payload = null,
        };

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.False(result.IsSuccess);
        factory.Verify(f => f.CreateAsync(It.IsAny<RedisCacheEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
