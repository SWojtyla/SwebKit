using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SwebKit.Agents.Tools.Storage;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;
using SwebKit.Core.Services;
using Xunit;

namespace SwebKit.Agents.Tests;

public class StorageToolsTests
{
    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    private static (AppStateService AppState, ProfileRepository Profiles) CreateContext(List<StorageConfig>? accounts = null)
    {
        var config = new AppConfig { Name = "Test" };
        if (accounts is not null) config.StorageAccounts = accounts;

        var repo = new ProfileRepository();
        repo.ReplaceProfileData(new ProfileData { Config = config });
        var appState = new AppStateService(repo, new UiStateRepository(), new AppEventBus(NullLogger<AppEventBus>.Instance));
        return (appState, repo);
    }

    private static StorageConfig Account(string id = "acc1", string name = "Prod Storage") => new()
    {
        Id = id,
        DisplayName = name,
        AccountName = "prodstore",
    };

    private static Mock<IStorageClientFactory> MakeFactory(IStorageClient client)
    {
        var factory = new Mock<IStorageClientFactory>();
        factory.Setup(f => f.Create(It.IsAny<StorageConfig>())).Returns(client);
        return factory;
    }

    // ── ListStorageBlobsTool ─────────────────────────────────────────────────

    [Fact]
    public async Task ListBlobs_NoAccountConfigured_ReturnsError()
    {
        var (appState, profiles) = CreateContext();
        var tool = new ListStorageBlobsTool(appState, profiles, Mock.Of<IStorageClientFactory>());

        var result = await tool.ExecuteAsync(Args("""{"container_name":"c1"}"""), CancellationToken.None);

        Assert.Contains("not configured", JsonDocument.Parse(result).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ListBlobs_MissingContainerName_ReturnsError_WithoutTouchingTheClient()
    {
        var client = new Mock<IStorageClient>();
        var (appState, profiles) = CreateContext([Account()]);
        var tool = new ListStorageBlobsTool(appState, profiles, MakeFactory(client.Object).Object);

        var result = await tool.ExecuteAsync(Args("{}"), CancellationToken.None);

        Assert.Contains("Missing required parameter", JsonDocument.Parse(result).RootElement.GetProperty("error").GetString());
        client.Verify(c => c.ListBlobsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListBlobs_ReturnsItemsAndContinuationToken()
    {
        var client = new Mock<IStorageClient>();
        client.Setup(c => c.ListBlobsAsync("c1", "", null, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageBlobPage(
                [new StorageBlobItem("a.txt", false, 100, "text/plain", DateTimeOffset.UtcNow, "etag")],
                "next-token"));
        var (appState, profiles) = CreateContext([Account()]);
        var tool = new ListStorageBlobsTool(appState, profiles, MakeFactory(client.Object).Object);

        var result = await tool.ExecuteAsync(Args("""{"container_name":"c1"}"""), CancellationToken.None);

        var root = JsonDocument.Parse(result).RootElement;
        Assert.Equal("next-token", root.GetProperty("continuation_token").GetString());
        Assert.Equal(1, root.GetProperty("items").GetArrayLength());
    }

    // ── GetStorageBlobPropertiesTool ─────────────────────────────────────────

    [Fact]
    public async Task GetBlobProperties_ReturnsSizeContentTypeAndMetadata()
    {
        var client = new Mock<IStorageClient>();
        client.Setup(c => c.GetBlobPropertiesAsync("c1", "a.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlobProperties(
                "a.txt", 512, "text/plain", DateTimeOffset.UtcNow, "etag123",
                LeaseStatus: null, LeaseState: null, AccessTier: "Hot", AccessTierInferred: true,
                ContentEncoding: null, ContentLanguage: null, CacheControl: null,
                Metadata: new Dictionary<string, string> { ["owner"] = "team-a" },
                Tags: new Dictionary<string, string>()));
        var (appState, profiles) = CreateContext([Account()]);
        var tool = new GetStorageBlobPropertiesTool(appState, profiles, MakeFactory(client.Object).Object);

        var result = await tool.ExecuteAsync(Args("""{"container_name":"c1","blob_name":"a.txt"}"""), CancellationToken.None);

        var root = JsonDocument.Parse(result).RootElement;
        Assert.Equal(512, root.GetProperty("size_bytes").GetInt64());
        Assert.Equal("Hot", root.GetProperty("access_tier").GetString());
    }

    [Fact]
    public async Task GetBlobProperties_MissingBlobName_ReturnsError()
    {
        var (appState, profiles) = CreateContext([Account()]);
        var tool = new GetStorageBlobPropertiesTool(appState, profiles, Mock.Of<IStorageClientFactory>());

        var result = await tool.ExecuteAsync(Args("""{"container_name":"c1"}"""), CancellationToken.None);

        Assert.Contains("Missing required parameter", JsonDocument.Parse(result).RootElement.GetProperty("error").GetString());
    }

    // ── ProposeCopyBlobTool ──────────────────────────────────────────────────

    [Fact]
    public async Task ProposeCopyBlob_RegistersLowRiskAction_WithPayload()
    {
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeCopyBlobTool(coordinator);

        var result = await tool.ExecuteAsync(Args("""
            {"source_container":"c1","source_blob_name":"a.txt","destination_container":"c2","destination_blob_name":"b.txt"}
            """), CancellationToken.None);

        var actionId = JsonDocument.Parse(result).RootElement.GetProperty("action_id").GetString()!;
        var action = coordinator.GetAction(actionId);
        Assert.NotNull(action);
        Assert.Equal(AgentActionType.CopyBlob, action!.Type);
        Assert.Equal(AgentActionRisk.Low, action.Risk);
        Assert.Equal("a.txt", action.Payload!.Value.GetProperty("source_blob_name").GetString());
    }

    [Fact]
    public async Task ProposeCopyBlob_MissingRequiredField_ReturnsError_WithoutRegisteringAnything()
    {
        var coordinator = new AgentActionCoordinator();
        var tool = new ProposeCopyBlobTool(coordinator);

        var result = await tool.ExecuteAsync(Args("""{"source_container":"c1"}"""), CancellationToken.None);

        Assert.Contains("Missing", result);
        Assert.Empty(coordinator.GetPendingActions());
    }
}
