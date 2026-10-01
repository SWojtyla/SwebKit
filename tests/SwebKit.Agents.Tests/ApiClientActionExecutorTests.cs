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
    public ApiRequestDetails? LastCreateDetails { get; private set; }
    public (string RequestId, string? Name, ApiRequestMethod? Method, string? Url)? LastUpdate { get; private set; }
    public ApiRequestDetails? LastUpdateDetails { get; private set; }
    public string? LastDuplicateRequestId { get; private set; }
    public (string RequestId, string? FolderPath, int? NewIndex)? LastMove { get; private set; }
    public string? LastDeleteRequestId { get; private set; }

    public ApiClientMutationResult NextResult { get; set; } = new() { IsSuccess = true, RequestId = "new-id" };
    public ApiRequestSnapshot? SnapshotToReturn { get; set; }

    public List<ApiRequestSummary> RequestsToReturn { get; } = [];

    public Task<IReadOnlyList<ApiRequestSummary>> SearchRequestsAsync(string? query = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ApiRequestSummary>>(RequestsToReturn);

    public Task<ApiRequestSnapshot?> GetRequestAsync(string requestId, CancellationToken ct = default) =>
        Task.FromResult(SnapshotToReturn);

    public Task<ApiClientMutationResult> CreateRequestAsync(string collectionId, string? folderPath, string name, ApiRequestMethod method, string url, ApiRequestDetails? details = null, CancellationToken ct = default)
    {
        LastCreate = (collectionId, folderPath, name, method, url);
        LastCreateDetails = details;
        return Task.FromResult(NextResult);
    }

    public Task<ApiClientMutationResult> UpdateRequestAsync(string requestId, string? name = null, ApiRequestMethod? method = null, string? url = null, ApiRequestDetails? details = null, CancellationToken ct = default)
    {
        LastUpdate = (requestId, name, method, url);
        LastUpdateDetails = details;
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

    public (string CollectionId, string Key, string? Value, VariableGeneratorKind? Generator, bool Enabled)? LastSetVariable { get; private set; }

    public Task<ApiClientMutationResult> SetCollectionVariableAsync(string collectionIdOrName, string key, string? value = null, VariableGeneratorKind? generator = null, bool enabled = true, CancellationToken ct = default)
    {
        LastSetVariable = (collectionIdOrName, key, value, generator, enabled);
        return Task.FromResult(NextResult);
    }

    public List<ApiCollectionSummary> CollectionsToReturn { get; } = [];

    public Task<IReadOnlyList<ApiCollectionSummary>> GetCollectionsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ApiCollectionSummary>>(CollectionsToReturn);
}

/// <summary>Minimal in-memory credential store for asserting where auth secrets land.</summary>
internal sealed class FakeCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> _secrets = [];

    public void Save(string key, string secret) => _secrets[key] = secret;
    public string? Get(string key) => _secrets.TryGetValue(key, out var v) ? v : null;
    public void Delete(string key) => _secrets.Remove(key);
    public IReadOnlyList<string> ListKeys(string prefix = "") => _secrets.Keys.Where(k => k.StartsWith(prefix)).ToList();
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
            AgentActionType.DeleteFolder, AgentActionType.SetCollectionVariable, AgentActionType.ExecuteHttpRequest,
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
    public async Task ApplyAsync_Create_ForwardsFullRequestDetails_FromPayload()
    {
        var apiClient = new FakeApiClientAgentService();
        var executor = new ApiClientActionExecutor(apiClient);
        var action = ActionWithPayload(AgentActionType.CreateRequest, "Collection c1", new
        {
            operation = "create",
            collection_id = "c1",
            name = "Get token",
            method = "Post",
            url = "https://api.example.com/token",
            headers = new[] { new { key = "Accept", value = "application/json" } },
            query_params = new[] { new { key = "v", value = "2" } },
            body_mode = "json",
            body = """{"user":"{{username}}"}""",
            capture_rules = new[]
            {
                new { target_variable = "token", source = "bodyJsonPath", json_path = "$.access_token" },
            },
        });

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var details = Assert.IsType<ApiRequestDetails>(apiClient.LastCreateDetails);
        var header = Assert.Single(details.Headers!);
        Assert.Equal("Accept", header.Key);
        Assert.Equal("application/json", header.Value);
        Assert.Equal("v", Assert.Single(details.QueryParams!).Key);
        Assert.Equal(RequestBodyMode.Json, details.Body!.Mode);
        Assert.Equal("""{"user":"{{username}}"}""", details.Body.RawContent);
        Assert.Equal("application/json", details.Body.ContentType);
        var rule = Assert.Single(details.CaptureRules!);
        Assert.Equal("token", rule.TargetVariable);
        Assert.Equal(CaptureSource.BodyJsonPath, rule.Source);
        Assert.Equal("$.access_token", rule.JsonPath);
        Assert.Equal("collection", rule.TargetScope);
    }

    [Fact]
    public async Task ApplyAsync_Create_CredentialSecret_IsStoredInCredentialStore_NotInAuthConfig()
    {
        var apiClient = new FakeApiClientAgentService();
        var credentials = new FakeCredentialStore();
        var executor = new ApiClientActionExecutor(apiClient, credentials);
        var action = ActionWithPayload(AgentActionType.CreateRequest, "Collection c1", new
        {
            operation = "create",
            collection_id = "c1",
            name = "Secure call",
            auth = new { type = "bearerToken", credential_secret = "hunter2" },
        });

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auth = apiClient.LastCreateDetails!.Auth!;
        Assert.Equal(AuthType.BearerToken, auth.Type);
        Assert.StartsWith("sw-secret:", auth.CredentialKey);
        // collections.json only ever sees the generated key — the plaintext lives in the OS store.
        Assert.Equal("hunter2", credentials.Get(auth.CredentialKey!));
        Assert.Null(auth.CredentialSecret);
    }

    [Fact]
    public async Task ApplyAsync_Update_ForwardsDetailFields_FromPayload()
    {
        var apiClient = new FakeApiClientAgentService();
        var executor = new ApiClientActionExecutor(apiClient);
        var action = ActionWithPayload(AgentActionType.UpdateRequest, "Request 'Old' (r1)", new
        {
            operation = "update",
            request_id = "r1",
            capture_rules = new[]
            {
                new { target_variable = "etag", source = "responseHeader", header_name = "ETag" },
            },
            auth = new { type = "apiKey", api_key_param_name = "X-Api-Key", credential_key = "sw-secret:existing" },
        });

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var details = Assert.IsType<ApiRequestDetails>(apiClient.LastUpdateDetails);
        var rule = Assert.Single(details.CaptureRules!);
        Assert.Equal("etag", rule.TargetVariable);
        Assert.Equal(CaptureSource.ResponseHeader, rule.Source);
        Assert.Equal("ETag", rule.HeaderName);
        Assert.Equal(AuthType.ApiKey, details.Auth!.Type);
        Assert.Equal("X-Api-Key", details.Auth.ApiKeyParamName);
        // A referenced existing key is passed through untouched — no new secret is minted.
        Assert.Equal("sw-secret:existing", details.Auth.CredentialKey);
        Assert.Null(details.Headers); // absent means "leave untouched", not "clear"
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
[Fact]
    public async Task ApplyAsync_SetCollectionVariable_PassesFieldsToService()
    {
        var apiClient = new FakeApiClientAgentService();
        var executor = new ApiClientActionExecutor(apiClient);
        var action = ActionWithPayload(AgentActionType.SetCollectionVariable, "Collection Sign", new
        {
            collection_id = "c1",
            key = "baseUrl",
            value = "https://dev-sign-api.eu",
        });

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(("c1", "baseUrl", "https://dev-sign-api.eu", (VariableGeneratorKind?)null, true), apiClient.LastSetVariable);
    }

    [Fact]
    public async Task ApplyAsync_SetCollectionVariable_GeneratorOverridesStaticValue()
    {
        var apiClient = new FakeApiClientAgentService();
        var executor = new ApiClientActionExecutor(apiClient);
        var action = ActionWithPayload(AgentActionType.SetCollectionVariable, "Collection Sign", new
        {
            collection_id = "Sign",
            key = "pkg",
            generator = "guid",
        });

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(VariableGeneratorKind.Guid, apiClient.LastSetVariable?.Generator);
        Assert.Null(apiClient.LastSetVariable?.Value);
    }

    [Fact]
    public async Task ApplyAsync_SetCollectionVariable_MissingKey_Fails()
    {
        var executor = new ApiClientActionExecutor(new FakeApiClientAgentService());
        var action = ActionWithPayload(AgentActionType.SetCollectionVariable, "Collection Sign", new
        {
            collection_id = "c1",
        });

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("key", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyAsync_Create_FormDataFileType_MarksFieldAsFile()
    {
        var apiClient = new FakeApiClientAgentService();
        var executor = new ApiClientActionExecutor(apiClient);
        var action = ActionWithPayload(AgentActionType.CreateRequest, "Collection Sign", new
        {
            collection_id = "c1",
            name = "Upload doc",
            method = "Post",
            url = "{{baseUrl}}/packages/{{pkg}}/documents",
            body_mode = "formData",
            form_data = new object[]
            {
                new { key = "document", value = "{}", type = "text" },
                new { key = "file", value = "C:/docs/test.pdf", type = "file" },
            },
        });

        var result = await executor.ApplyAsync(action, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var fields = apiClient.LastCreateDetails?.Body?.FormData;
        Assert.NotNull(fields);
        Assert.False(fields![0].IsFile);
        Assert.True(fields[1].IsFile);
        Assert.Equal("C:/docs/test.pdf", fields[1].Value);
    }
}
