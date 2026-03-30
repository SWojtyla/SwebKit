using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.DevOps.Tests.Fakes;

namespace SwebKit.DevOps.Tests;

public sealed class DevOpsClientTests
{
  // ── Factory helper ──────────────────────────────────────────────────────
  // DevOpsClient(IHttpClientFactory, DevOpsAuthHandler, ILogger).
  // DevOpsAuthHandler is a DelegatingHandler; we set its InnerHandler to the
  // FakeHttpMessageHandler so the fake receives every outgoing request.

  private static (DevOpsClient client, FakeHttpMessageHandler handler) CreateClient(
      string organization = "testorg",
      string patCredentialKey = "devops:pat")
  {
    var fakeHandler = new FakeHttpMessageHandler();
    var credentialStore = new FakeCredentialStore();
    credentialStore.Seed(patCredentialKey, "test-pat-value");

    var authHandler = new DevOpsAuthHandler(credentialStore)
    {
      InnerHandler = fakeHandler
    };

    var httpClient = new HttpClient(authHandler)
    {
      BaseAddress = null
    };

    var factory = new FakeHttpClientFactory(httpClient);
    var client = new DevOpsClient(factory, authHandler, NullLogger<DevOpsClient>.Instance);

    client.Configure(new DevOpsConfig
    {
      Organization = organization,
      PatCredentialKey = patCredentialKey
    });

    return (client, fakeHandler);
  }

  // ── Configure() ─────────────────────────────────────────────────────────

  [Fact]
  public async Task Configure_CalledTwice_UsesUpdatedOrganizationWithoutThrowing()
  {
    var (client, handler) = CreateClient(organization: "firstorg");

    handler.EnqueueJson("{\"count\":1,\"value\":[]}", HttpStatusCode.OK);
    handler.EnqueueJson("{\"count\":1,\"value\":[]}", HttpStatusCode.OK);

    var firstResult = await client.TestConnectionAsync(CancellationToken.None);
    Assert.True(firstResult);

    var ex = Record.Exception(() => client.Configure(new DevOpsConfig
    {
      Organization = "secondorg",
      PatCredentialKey = "devops:pat"
    }));
    Assert.Null(ex);

    var secondResult = await client.TestConnectionAsync(CancellationToken.None);
    Assert.True(secondResult);

    Assert.Equal(2, handler.RequestUris.Count);
    Assert.Equal("https://dev.azure.com/firstorg/_apis/projects", handler.RequestUris[0]!.GetLeftPart(UriPartial.Path));
    Assert.Equal("https://dev.azure.com/secondorg/_apis/projects", handler.RequestUris[1]!.GetLeftPart(UriPartial.Path));
  }

  [Fact]
  public async Task Configure_CalledTwice_UsesUpdatedPatCredentialKey()
  {
    var fakeHandler = new FakeHttpMessageHandler();
    var credentialStore = new FakeCredentialStore();
    credentialStore.Seed("pat:key:1", "pat-value-1");
    credentialStore.Seed("pat:key:2", "pat-value-2");

    var authHandler = new DevOpsAuthHandler(credentialStore) { InnerHandler = fakeHandler };
    var factory = new FakeHttpClientFactory(new HttpClient(authHandler));
    var client = new DevOpsClient(factory, authHandler, NullLogger<DevOpsClient>.Instance);

    client.Configure(new DevOpsConfig
    {
      Organization = "myorg",
      PatCredentialKey = "pat:key:1"
    });

    fakeHandler.EnqueueJson("{\"count\":1,\"value\":[]}", HttpStatusCode.OK);
    var firstResult = await client.TestConnectionAsync(CancellationToken.None);
    Assert.True(firstResult);

    client.Configure(new DevOpsConfig
    {
      Organization = "myorg",
      PatCredentialKey = "pat:key:2"
    });

    fakeHandler.EnqueueJson("{\"count\":1,\"value\":[]}", HttpStatusCode.OK);
    var secondResult = await client.TestConnectionAsync(CancellationToken.None);
    Assert.True(secondResult);

    var expectedFirstAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes(":pat-value-1"));
    var expectedSecondAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes(":pat-value-2"));

    Assert.Equal(expectedFirstAuth, fakeHandler.AuthorizationParameters[0]);
    Assert.Equal(expectedSecondAuth, fakeHandler.AuthorizationParameters[1]);
  }

  [Theory]
  [InlineData("myorg", "https://dev.azure.com/myorg/_apis/projects")]
  [InlineData("https://dev.azure.com/myorg", "https://dev.azure.com/myorg/_apis/projects")]
  [InlineData("https://myorg.visualstudio.com", "https://myorg.visualstudio.com/_apis/projects")]
  public async Task Configure_NormalizesOrganizationInput(string organizationInput, string expectedProjectsPath)
  {
    var (client, handler) = CreateClient(organization: organizationInput);

    handler.EnqueueJson("{\"count\":1,\"value\":[]}", HttpStatusCode.OK);

    var result = await client.TestConnectionAsync(CancellationToken.None);
    Assert.True(result);

    Assert.NotNull(handler.LastRequestUri);
    Assert.Equal(
        expectedProjectsPath,
        handler.LastRequestUri!.GetLeftPart(UriPartial.Path),
        StringComparer.OrdinalIgnoreCase);
    Assert.DoesNotContain("https://dev.azure.com/https://", handler.LastRequestUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void Configure_FirstCall_DoesNotThrow()
  {
    var fakeHandler = new FakeHttpMessageHandler();
    var credentialStore = new FakeCredentialStore();
    var authHandler = new DevOpsAuthHandler(credentialStore) { InnerHandler = fakeHandler };
    var factory = new FakeHttpClientFactory(new HttpClient(authHandler));
    var client = new DevOpsClient(factory, authHandler, NullLogger<DevOpsClient>.Instance);

    var ex = Record.Exception(() => client.Configure(
        new DevOpsConfig { Organization = "myorg", PatCredentialKey = "key" }));

    Assert.Null(ex);
  }

  // ── DevOpsConfig.Validate() ──────────────────────────────────────────────

  [Fact]
  public void DevOpsConfig_Validate_EmptyOrganization_Throws()
  {
    var config = new DevOpsConfig { Organization = string.Empty, PatCredentialKey = "key" };

    var ex = Assert.Throws<InvalidOperationException>(config.Validate);
    Assert.Contains(nameof(DevOpsConfig.Organization), ex.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void DevOpsConfig_Validate_EmptyPatCredentialKey_Throws()
  {
    var config = new DevOpsConfig { Organization = "myorg", PatCredentialKey = string.Empty };

    var ex = Assert.Throws<InvalidOperationException>(config.Validate);
    Assert.Contains(nameof(DevOpsConfig.PatCredentialKey), ex.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void DevOpsConfig_Validate_ValidConfig_DoesNotThrow()
  {
    var config = new DevOpsConfig { Organization = "myorg", PatCredentialKey = "devops:pat" };

    var ex = Record.Exception(config.Validate);

    Assert.Null(ex);
  }

  // ── TestConnectionAsync ──────────────────────────────────────────────────

  [Fact]
  public async Task TestConnectionAsync_SuccessResponse_ReturnsTrue()
  {
    var (client, handler) = CreateClient();
    handler.EnqueueJson("{\"count\":1,\"value\":[]}", HttpStatusCode.OK);

    var result = await client.TestConnectionAsync(CancellationToken.None);

    Assert.True(result);
  }

  [Fact]
  public async Task TestConnectionAsync_ErrorResponse_ReturnsFalse()
  {
    var (client, handler) = CreateClient();
    handler.EnqueueJson("{}", HttpStatusCode.Unauthorized);

    var result = await client.TestConnectionAsync(CancellationToken.None);

    Assert.False(result);
  }

  // ── GetProjectsAsync ─────────────────────────────────────────────────────

  [Fact]
  public async Task GetProjectsAsync_ValidResponse_ReturnsMappedProjects()
  {
    var (client, handler) = CreateClient();
    handler.EnqueueJson("""
            {
              "count": 2,
              "value": [
                { "id": "proj-id-1", "name": "Alpha" },
                { "id": "proj-id-2", "name": "Beta" }
              ]
            }
            """);

    var projects = await client.GetProjectsAsync(CancellationToken.None);

    Assert.Equal(2, projects.Count);
    Assert.Equal("Alpha", projects[0].Name);
    Assert.Equal("proj-id-1", projects[0].Id);
    Assert.Equal("Beta", projects[1].Name);
  }

  [Fact]
  public async Task GetProjectsAsync_EmptyResponse_ReturnsEmptyList()
  {
    var (client, handler) = CreateClient();
    handler.EnqueueJson("""{"count":0,"value":[]}""");

    var projects = await client.GetProjectsAsync(CancellationToken.None);

    Assert.Empty(projects);
  }

  // ── GetPipelinesAsync ────────────────────────────────────────────────────

  [Fact]
  public async Task GetPipelinesAsync_ValidResponse_ReturnsMappedPipelines()
  {
    var (client, handler) = CreateClient();
    handler.EnqueueJson("""
            {
              "count": 1,
              "value": [
                { "id": 42, "name": "CI Build", "folder": "\\Builds", "url": "https://dev.azure.com/testorg/TestProject/_apis/pipelines/42" }
              ]
            }
            """);

    var pipelines = await client.GetPipelinesAsync("TestProject", CancellationToken.None);

    Assert.Single(pipelines);
    Assert.Equal(42, pipelines[0].Id);
    Assert.Equal("CI Build", pipelines[0].Name);
  }

  [Fact]
  public async Task GetPipelinesAsync_NullResponse_ReturnsEmptyList()
  {
    var (client, handler) = CreateClient();
    // GetFromJsonAsync returns null when the body is "null"
    handler.EnqueueJson("null");

    var pipelines = await client.GetPipelinesAsync("TestProject", CancellationToken.None);

    Assert.Empty(pipelines);
  }

  // ── GetPipelineRunsAsync ──────────────────────────────────────────────────

  [Fact]
  public async Task GetPipelineRunsAsync_ValidResponse_ReturnsMappedRuns()
  {
    var (client, handler) = CreateClient();
    handler.EnqueueJson("""
            {
              "count": 1,
              "value": [
                {
                  "id": 100,
                  "name": "20240101.1",
                  "state": "completed",
                  "result": "succeeded",
                  "createdDate": "2024-01-01T10:00:00Z",
                  "pipeline": { "id": 42, "name": "CI Build" },
                  "resources": {
                    "repositories": {
                      "self": { "refName": "refs/heads/main" }
                    }
                  }
                }
              ]
            }
            """);

    var runs = await client.GetPipelineRunsAsync("TestProject", 42, ct: CancellationToken.None);

    Assert.Single(runs);
    var run = runs[0];
    Assert.Equal(100, run.Id);
    Assert.Equal("completed", run.State);
    Assert.Equal("succeeded", run.Result);
    Assert.Equal("main", run.SourceBranch);
  }

  [Fact]
  public async Task GetPipelineRunsAsync_EmptyResponse_ReturnsEmptyList()
  {
    var (client, handler) = CreateClient();
    handler.EnqueueJson("""{"count":0,"value":[]}""");

    var runs = await client.GetPipelineRunsAsync("TestProject", 1, ct: CancellationToken.None);

    Assert.Empty(runs);
  }

  // ── GetWaitingStagesAsync ─────────────────────────────────────────────────

  [Fact]
  public async Task GetWaitingStagesAsync_HttpError_ReturnsEmptyList()
  {
    var (client, handler) = CreateClient();
    handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.InternalServerError));

    var result = await client.GetWaitingStagesAsync("TestProject", 99, CancellationToken.None);

    Assert.Empty(result);
  }

  [Fact]
  public async Task GetWaitingStagesAsync_EmptyTimeline_ReturnsEmptyList()
  {
    var (client, handler) = CreateClient();
    handler.EnqueueJson("""{"records":[]}""");

    var result = await client.GetWaitingStagesAsync("TestProject", 1, CancellationToken.None);

    Assert.Empty(result);
  }

  [Fact]
  public async Task GetWaitingStagesAsync_NoCheckpoints_ReturnsEmptyList()
  {
    var (client, handler) = CreateClient();
    // Timeline has only Stage records, no Checkpoint records
    handler.EnqueueJson("""
            {
              "records": [
                { "id": "stage-1", "type": "Stage", "name": "Deploy", "state": "completed", "result": "succeeded", "order": 1 }
              ]
            }
            """);

    var result = await client.GetWaitingStagesAsync("TestProject", 1, CancellationToken.None);

    Assert.Empty(result);
  }

  [Fact]
  public async Task GetWaitingStagesAsync_StageWithInProgressCheckpoint_ReturnsWaitingStage()
  {
    var (client, handler) = CreateClient();
    // Timeline: Stage → inProgress Checkpoint (parentId = stage) → Checkpoint.Approval
    handler.EnqueueJson("""
            {
              "records": [
                { "id": "stage-1", "type": "Stage", "name": "Production Deploy", "state": "inProgress", "order": 2, "parentId": null },
                { "id": "chk-1", "type": "Checkpoint", "state": "inProgress", "parentId": "stage-1", "order": 1 },
                { "id": "approval-1", "type": "Checkpoint.Approval", "state": "inProgress", "parentId": "chk-1", "order": 1 }
              ]
            }
            """);

    var result = await client.GetWaitingStagesAsync("TestProject", 1, CancellationToken.None);

    Assert.Single(result);
    Assert.Equal("Production Deploy", result[0].StageName);
    Assert.Equal("approval-1", result[0].ApprovalId);
  }

  // ── GetPendingApprovalsAsync ──────────────────────────────────────────────

  [Fact]
  public async Task GetPendingApprovalsAsync_HttpError_ReturnsEmptyList()
  {
    var (client, handler) = CreateClient();
    handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.Forbidden));

    var result = await client.GetPendingApprovalsAsync("TestProject", CancellationToken.None);

    Assert.Empty(result);
  }

  [Fact]
  public async Task GetPendingApprovalsAsync_PendingStatus_ReturnedInList()
  {
    var (client, handler) = CreateClient();
    handler.EnqueueJson("""
            {
              "count": 1,
              "value": [
                {
                  "id": "approval-abc",
                  "status": "pending",
                  "createdOn": "2024-01-01T10:00:00Z",
                  "pipeline": { "id": 10, "name": "Release" },
                  "steps": [
                    { "status": "pending", "assignedApprover": { "displayName": "Jane Dev" } }
                  ]
                }
              ]
            }
            """);

    var result = await client.GetPendingApprovalsAsync("TestProject", CancellationToken.None);

    Assert.Single(result);
    Assert.Equal("approval-abc", result[0].Id);
    Assert.Equal("pending", result[0].Status);
    Assert.Equal("Jane Dev", result[0].TriggeredBy);
  }

  [Fact]
  public async Task GetPendingApprovalsAsync_ApprovedStatus_FilteredOut()
  {
    var (client, handler) = CreateClient();
    // "approved" is not in the pending filter set
    handler.EnqueueJson("""
            {
              "count": 1,
              "value": [
                {
                  "id": "approval-xyz",
                  "status": "approved",
                  "createdOn": "2024-01-01T10:00:00Z"
                }
              ]
            }
            """);

    var result = await client.GetPendingApprovalsAsync("TestProject", CancellationToken.None);

    Assert.Empty(result);
  }

  // ── GetRepositoriesAsync ──────────────────────────────────────────────────

  [Fact]
  public async Task GetRepositoriesAsync_ValidResponse_ReturnsMappedRepos()
  {
    var (client, handler) = CreateClient();
    handler.EnqueueJson("""
            {
              "count": 1,
              "value": [
                { "id": "repo-id-1", "name": "MyRepo", "defaultBranch": "refs/heads/main", "webUrl": "https://dev.azure.com/testorg/TestProject/_git/MyRepo" }
              ]
            }
            """);

    var repos = await client.GetRepositoriesAsync("TestProject", CancellationToken.None);

    Assert.Single(repos);
    Assert.Equal("repo-id-1", repos[0].Id);
    Assert.Equal("MyRepo", repos[0].Name);
  }

  // ── GetEnvironmentsAsync ──────────────────────────────────────────────────

  [Fact]
  public async Task GetEnvironmentsAsync_ValidResponse_ReturnsMappedEnvironments()
  {
    var (client, handler) = CreateClient();
    handler.EnqueueJson("""
            {
              "count": 2,
              "value": [
                { "id": 1, "name": "Staging" },
                { "id": 2, "name": "Production" }
              ]
            }
            """);

    var environments = await client.GetEnvironmentsAsync("TestProject", CancellationToken.None);

    Assert.Equal(2, environments.Count);
    Assert.Equal(1, environments[0].Id);
    Assert.Equal("Staging", environments[0].Name);
    Assert.Equal("Production", environments[1].Name);
  }

  // ── GetTagsAsync ──────────────────────────────────────────────────────────

  [Fact]
  public async Task GetTagsAsync_ValidResponse_ReturnsTags()
  {
    var (client, handler) = CreateClient();
    handler.EnqueueJson("""
            {
              "count": 2,
              "value": [
                { "name": "refs/tags/v1.1.0", "objectId": "sha-b", "creator": { "displayName": "Dev A" } },
                { "name": "refs/tags/v1.0.0", "objectId": "sha-a", "creator": { "displayName": "Dev B" } }
              ]
            }
            """);

    var tags = await client.GetTagsAsync("TestProject", "repo-id-1", CancellationToken.None);

    Assert.Equal(2, tags.Count);
    // Tags are ordered descending by name — v1.1.0 first
    Assert.Equal("v1.1.0", tags[0].Name);
    Assert.Equal("v1.0.0", tags[1].Name);
  }

  // ── GetCommitsAsync ───────────────────────────────────────────────────────

  [Fact]
  public async Task GetCommitsAsync_ValidResponse_ReturnsMappedCommits()
  {
    var (client, handler) = CreateClient();
    handler.EnqueueJson("""
            {
              "count": 1,
              "value": [
                {
                  "commitId": "abcdef1234567",
                  "comment": "Fix nasty bug",
                  "author": { "name": "Alice", "date": "2024-06-01T12:00:00Z" }
                }
              ]
            }
            """);

    var commits = await client.GetCommitsAsync("TestProject", "repo-1", "main", ct: CancellationToken.None);

    Assert.Single(commits);
    Assert.Equal("abcdef1234567", commits[0].CommitId);
    Assert.Equal("abcdef1", commits[0].ShortId);
    Assert.Equal("Fix nasty bug", commits[0].Comment);
    Assert.Equal("Alice", commits[0].AuthorName);
  }
}
