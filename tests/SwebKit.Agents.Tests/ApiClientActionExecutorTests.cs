using System.Text.Json;
using SwebKit.Agents.Tools.ApiClient;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using Xunit;

namespace SwebKit.Agents.Tests;

/// <summary>Records every call made to it; each method returns a canned, configurable result.</summary>
internal sealed class FakeApiClientAgentService : IApiClientAgentService
{
    public (string CollectionId, string? FolderPath, string Name, ApiRequestMethod Method, string Url)? LastCreate { get; private set; }
    public (string RequestId, string? Name, ApiRequestMethod? Method, string? Url)? LastUpdate { get; private set; }
    public string? LastDuplicateRequestId { get; private set; }
    public (string RequestId, string? FolderPath, int? NewIndex)? LastMove { get; private set; }
    public string? LastDeleteRequestId { get; private set; }

    public ApiClientMutationResult NextResult { get; set; } = new() { IsSuccess = true, RequestId = "new-id" };
    public ApiRequestSnapshot? SnapshotToReturn { get; set; }

    public Task<IReadOnlyList<ApiRequestSummary>> SearchRequestsAsync(string? query = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ApiRequestSummary>>([]);

    public Task<ApiRequestSnapshot?> GetRequestAsync(string requestId, CancellationToken ct = default) =>
        Task.FromResult(SnapshotToReturn);

    public Task<ApiClientMutationResult> CreateRequestAsync(string collectionId, string? folderPath, string name, ApiRequestMethod method, string url, CancellationToken ct = default)
    {
        LastCreate = (collectionId, folderPath, name, method, url);
        return Task.FromResult(NextResult);
    }

    public Task<ApiClientMutationResult> UpdateRequestAsync(string requestId, string? name = null, ApiRequestMethod? method = null, string? url = null, CancellationToken ct = default)
    {
        LastUpdate = (requestId, name, method, url);
        return Task.FromResult(NextResult);
    }

    public Task<ApiClientMutationResult> DuplicateRequestAsync(string requestId, CancellationToken ct = default)
    {
        LastDuplicateRequestId = requestId;
        return Task.FromResult(NextResult);
    }

    public Task<ApiClientMutationResult> MoveRequestAsync(string requestId, string? targetFolderPath, int? newIndex, CancellationToken ct = default)
    {
        LastMove = (requestId, targetFolderPath, newIndex);
        return Task.FromResult(NextResult);
    }

    public Task<ApiClientMutationResult> RenameFolderAsync(string collectionId, string folderPath, string newName, CancellationToken ct = default) =>
        Task.FromResult(NextResult);

    public Task<ApiClientMutationResult> DeleteRequestAsync(string requestId, CancellationToken ct = default)
    {
        LastDeleteRequestId = requestId;
        return Task.FromResult(NextResult);
    }

    public Task<ApiClientMutationResult> DeleteFolderAsync(string collectionId, string folderPath, CancellationToken ct = default) =>
        Task.FromResult(NextResult);

    public Task<IReadOnlyList<(string Id, string Name, string Origin, string? LinkedRootId)>> GetCollectionsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<(string, string, string, string?)>>([]);
}

public class ApiClientActionExecutorTests
{
    private static PendingAgentAction ActionWithPayload(AgentActionType type, string target, object payload) => new()
    {
        Id = "a1",
        Type = type,
        Summary = "S",
        Target = target,
        Risk = AgentActionRisk.Low,
        Preview = "P",
        ExpectedFingerprint = null,
        Payload = JsonSerializer.SerializeToElement(payload),
    };

    [Fact]
    public void CanHandle_ReturnsTrueForEveryApiClientActionType()
    {
        var executor = new ApiClientActionExecutor(new FakeApiClientAgentService());

        foreach (var type in new[]
        {
            AgentActionType.CreateRequest, AgentActionType.UpdateRequest, AgentActionType.DeleteRequest,
            AgentActionType.DuplicateRequest, AgentActionType.MoveRequest, AgentActionType.RenameFolder,
            AgentActionType.DeleteFolder, AgentActionType.ExecuteHttpRequest,
        })
        {
            Assert.True(executor.CanHandle(type));
        }
    }

    [Fact]
    public async Task ApplyAsync_Create_CallsCreateRequestAsync_WithFieldsFromPayload()
    {
        var apiClient = new FakeApiClientAgentService();
        var executor = new ApiClientActionExecutor(apiClient);
        var action = ActionWithPayload(AgentActionType.CreateRequest, "Collection c1", new
        {
            operation = "create",
            collection_id = "c1",
            folder_path = "Auth",
            name = "Get token",
            method = "Post",
            url = "https://api.example.com/token",
        });

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(("c1", "Auth", "Get token", ApiRequestMethod.Post, "https://api.example.com/token"), apiClient.LastCreate);
    }

    [Fact]
    public async Task ApplyAsync_Create_MissingPayload_FailsCleanly_WithoutCallingTheClient()
    {
        var apiClient = new FakeApiClientAgentService();
        var executor = new ApiClientActionExecutor(apiClient);
        var action = new PendingAgentAction
        {
            Id = "a1",
            Type = AgentActionType.CreateRequest,
            Summary = "S",
            Target = "T",
            Risk = AgentActionRisk.Low,
            Preview = "P",
            ExpectedFingerprint = null,
            Payload = null,
        };

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(apiClient.LastCreate);
    }

    [Fact]
    public async Task ApplyAsync_Update_CallsUpdateRequestAsync_WithOnlyTheFieldsThatWereProposed()
    {
        var apiClient = new FakeApiClientAgentService();
        var executor = new ApiClientActionExecutor(apiClient);
        var action = ActionWithPayload(AgentActionType.UpdateRequest, "Request 'Old' (r1)", new
        {
            operation = "update",
            request_id = "r1",
            url = "https://api.example.com/v2",
        });

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(("r1", null, null, "https://api.example.com/v2"), apiClient.LastUpdate);
    }

    [Fact]
    public async Task ApplyAsync_Delete_UsesRequestIdFromTarget()
    {
        var apiClient = new FakeApiClientAgentService();
        var executor = new ApiClientActionExecutor(apiClient);
        var action = new PendingAgentAction
        {
            Id = "a1",
            Type = AgentActionType.DeleteRequest,
            Summary = "S",
            Target = "Request 'Get token' (r1)",
            Risk = AgentActionRisk.High,
            Preview = "P",
            ExpectedFingerprint = null,
        };

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("r1", apiClient.LastDeleteRequestId);
    }

    [Fact]
    public async Task ApplyAsync_Duplicate_UsesRequestIdFromTarget()
    {
        var apiClient = new FakeApiClientAgentService();
        var executor = new ApiClientActionExecutor(apiClient);
        var action = new PendingAgentAction
        {
            Id = "a1",
            Type = AgentActionType.DuplicateRequest,
            Summary = "S",
            Target = "Request 'Get token' (r1)",
            Risk = AgentActionRisk.Low,
            Preview = "P",
            ExpectedFingerprint = null,
        };

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("r1", apiClient.LastDuplicateRequestId);
    }

    [Fact]
    public async Task ApplyAsync_Move_CallsMoveRequestAsync_WithFieldsFromPayload()
    {
        var apiClient = new FakeApiClientAgentService();
        var executor = new ApiClientActionExecutor(apiClient);
        var action = ActionWithPayload(AgentActionType.MoveRequest, "Request 'Get token' (r1)", new
        {
            operation = "move",
            request_id = "r1",
            folder_path = "Archive",
            new_index = 2,
        });

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(("r1", "Archive", 2), apiClient.LastMove);
    }

    [Fact]
    public async Task ApplyAsync_ExecuteHttpRequest_FailsWithAClearNotImplementedMessage_WithoutSendingAnyRequest()
    {
        var apiClient = new FakeApiClientAgentService
        {
            SnapshotToReturn = new ApiRequestSnapshot
            {
                Id = "r1",
                Name = "Get token",
                CollectionId = "c1",
                CollectionName = "Auth API",
                CollectionOrigin = "local",
                LinkedRootId = null,
                FolderPath = null,
                Method = ApiRequestMethod.Post,
                Url = "https://api.example.com/token",
                UpdatedAt = DateTimeOffset.UtcNow,
            },
        };
        var executor = new ApiClientActionExecutor(apiClient);
        var action = new PendingAgentAction
        {
            Id = "a1",
            Type = AgentActionType.ExecuteHttpRequest,
            Summary = "Execute POST https://api.example.com/token",
            Target = "Request 'Get token' (r1)",
            Risk = AgentActionRisk.High,
            Preview = "P",
            ExpectedFingerprint = null,
        };

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("not implemented", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyAsync_ExecuteHttpRequest_FingerprintMismatch_FailsBeforeReachingTheNotImplementedPath()
    {
        var apiClient = new FakeApiClientAgentService
        {
            SnapshotToReturn = new ApiRequestSnapshot
            {
                Id = "r1",
                Name = "Get token",
                CollectionId = "c1",
                CollectionName = "Auth API",
                CollectionOrigin = "local",
                LinkedRootId = null,
                FolderPath = null,
                Method = ApiRequestMethod.Post,
                Url = "https://api.example.com/token",
                UpdatedAt = DateTimeOffset.UtcNow,
            },
        };
        var executor = new ApiClientActionExecutor(apiClient);
        var action = new PendingAgentAction
        {
            Id = "a1",
            Type = AgentActionType.ExecuteHttpRequest,
            Summary = "S",
            Target = "Request 'Get token' (r1)",
            Risk = AgentActionRisk.High,
            Preview = "P",
            ExpectedFingerprint = DateTimeOffset.UtcNow.AddDays(-1).ToString("O"), // stale on purpose
        };

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("changed since", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
