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

public class StorageActionExecutorTests
{
    private static (AppStateService AppState, ProfileRepository Profiles) CreateContext(StorageConfig account)
    {
        var config = new AppConfig { Name = "Test", StorageAccounts = [account] };
        var repo = new ProfileRepository();
        repo.ReplaceProfileData(new ProfileData { Config = config });
        return (new AppStateService(repo, new UiStateRepository(), new AppEventBus(NullLogger<AppEventBus>.Instance)), repo);
    }

    private static PendingAgentAction ActionFor(object payload) => new()
    {
        Id = "a1",
        Type = AgentActionType.CopyBlob,
        Summary = "S",
        Target = "T",
        Risk = AgentActionRisk.Low,
        Preview = "P",
        ExpectedFingerprint = null,
        Payload = JsonSerializer.SerializeToElement(payload),
    };

    [Fact]
    public void CanHandle_OnlyCopyBlob()
    {
        var executor = new StorageActionExecutor(
            new AppStateService(new ProfileRepository(), new UiStateRepository(), new AppEventBus(NullLogger<AppEventBus>.Instance)),
            new ProfileRepository(),
            Mock.Of<IStorageClientFactory>());

        Assert.True(executor.CanHandle(AgentActionType.CopyBlob));
        Assert.False(executor.CanHandle(AgentActionType.DeleteRedisKey));
    }

    [Fact]
    public async Task ApplyAsync_CallsCopyBlobAsync_WithExactFieldsFromPayload()
    {
        var client = new Mock<IStorageClient>();
        client.Setup(c => c.CopyBlobAsync(It.IsAny<BlobCopyOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlobMutationResult(Success: true, ResultBlobPath: "c2/b.txt"));
        var factory = new Mock<IStorageClientFactory>();
        var account = new StorageConfig { Id = "acc1", AccountName = "prodstore" };
        factory.Setup(f => f.Create(account)).Returns(client.Object);
        var (appState, profiles) = CreateContext(account);
        var executor = new StorageActionExecutor(appState, profiles, factory.Object);

        var result = await executor.ApplyAsync(ActionFor(new
        {
            source_container = "c1",
            source_blob_name = "a.txt",
            destination_container = "c2",
            destination_blob_name = "b.txt",
            overwrite = true,
        }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Copied to 'c2/b.txt'", result.ResultSummary);
        client.Verify(c => c.CopyBlobAsync(
            It.Is<BlobCopyOptions>(o => o.SourceContainer == "c1" && o.SourceBlobName == "a.txt" &&
                                         o.DestinationContainer == "c2" && o.DestinationBlobName == "b.txt" &&
                                         o.Overwrite),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_ClientReportsFailure_PropagatesTheErrorMessage()
    {
        var client = new Mock<IStorageClient>();
        client.Setup(c => c.CopyBlobAsync(It.IsAny<BlobCopyOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlobMutationResult(Success: false, ErrorMessage: "destination already exists"));
        var factory = new Mock<IStorageClientFactory>();
        var account = new StorageConfig { Id = "acc1" };
        factory.Setup(f => f.Create(account)).Returns(client.Object);
        var (appState, profiles) = CreateContext(account);
        var executor = new StorageActionExecutor(appState, profiles, factory.Object);

        var result = await executor.ApplyAsync(ActionFor(new
        {
            source_container = "c1",
            source_blob_name = "a.txt",
            destination_container = "c2",
            destination_blob_name = "b.txt",
        }), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("destination already exists", result.ErrorMessage);
    }

    [Fact]
    public async Task ApplyAsync_MissingRequiredField_FailsWithoutResolvingAnyClient()
    {
        var factory = new Mock<IStorageClientFactory>();
        var account = new StorageConfig { Id = "acc1" };
        var (appState, profiles) = CreateContext(account);
        var executor = new StorageActionExecutor(appState, profiles, factory.Object);

        var result = await executor.ApplyAsync(ActionFor(new { source_container = "c1" }), CancellationToken.None);

        Assert.False(result.IsSuccess);
        factory.Verify(f => f.Create(It.IsAny<StorageConfig>()), Times.Never);
    }
}
