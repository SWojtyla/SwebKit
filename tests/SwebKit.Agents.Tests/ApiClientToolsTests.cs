using System.Text.Json;
using SwebKit.Agents.Tools.ApiClient;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using Xunit;

namespace SwebKit.Agents.Tests;

/// <summary>
/// Covers the API Client read/discovery tools and the create-proposal preview —
/// the contract that lets the model name a target collection it has never seen the ID of.
/// </summary>
public class ApiClientToolsTests
{
    private static readonly JsonElement EmptyArgs = JsonDocument.Parse("{}").RootElement;

    private static ApiCollectionSummary Collection(
        string id, string name, IReadOnlyList<string>? folders = null, int requestCount = 0) => new()
    {
        Id = id,
        Name = name,
        Origin = "local",
        LinkedRootId = null,
        FolderPaths = folders ?? [],
        RequestCount = requestCount,
    };

    // ── list_api_collections ──────────────────────────────────────────────────

    [Fact]
    public async Task ListApiCollections_ReturnsIdsNamesFoldersAndCounts()
    {
        var apiClient = new FakeApiClientAgentService();
        apiClient.CollectionsToReturn.Add(Collection("c1", "Phone Notification", ["Signing", "Signing/Onboarding"], requestCount: 7));
        var tool = new ListApiCollectionsTool(apiClient);

        using var doc = JsonDocument.Parse(await tool.ExecuteAsync(EmptyArgs, CancellationToken.None));

        var collection = doc.RootElement.GetProperty("collections")[0];
        Assert.Equal("c1", collection.GetProperty("id").GetString());
        Assert.Equal("Phone Notification", collection.GetProperty("name").GetString());
        Assert.Equal("local", collection.GetProperty("origin").GetString());
        Assert.Equal(7, collection.GetProperty("request_count").GetInt32());
        Assert.Equal(
            new[] { "Signing", "Signing/Onboarding" },
            collection.GetProperty("folders").EnumerateArray().Select(f => f.GetString()!).ToArray());
    }

    [Fact]
    public async Task ListApiCollections_Empty_ExplainsACollectionIsCreatedOnConfirm()
    {
        var tool = new ListApiCollectionsTool(new FakeApiClientAgentService());

        var output = await tool.ExecuteAsync(EmptyArgs, CancellationToken.None);

        Assert.Contains("created on confirm", output, StringComparison.OrdinalIgnoreCase);
    }

    // ── search_api_requests ───────────────────────────────────────────────────

    [Fact]
    public async Task SearchApiRequests_IncludesCollectionIdSoTheModelCanTargetCreates()
    {
        var apiClient = new FakeApiClientAgentService();
        apiClient.RequestsToReturn.Add(new ApiRequestSummary
        {
            Id = "r1",
            Name = "Get token",
            CollectionId = "c1",
            CollectionName = "Auth API",
            CollectionOrigin = "local",
            LinkedRootId = null,
            FolderPath = "Tokens",
            Method = ApiRequestMethod.Post,
            Url = "https://api.example.com/token",
        });
        var tool = new SearchApiRequestsTool(apiClient);

        using var doc = JsonDocument.Parse(await tool.ExecuteAsync(EmptyArgs, CancellationToken.None));

        var request = doc.RootElement.GetProperty("requests")[0];
        Assert.Equal("c1", request.GetProperty("collection_id").GetString());
        Assert.Equal("Auth API", request.GetProperty("collection").GetString());
    }

    // ── propose_api_request_change create previews ────────────────────────────

    [Fact]
    public async Task ProposeCreate_UnknownCollection_PreviewSaysItWillBeCreated()
    {
        var apiClient = new FakeApiClientAgentService();
        var tool = new ProposeApiRequestChangeTool(apiClient, new AgentActionCoordinator());

        var output = await tool.ExecuteAsync(JsonDocument.Parse("""
            {"operation":"create","collection_id":"Phone Notification","folder_path":"Signing",
             "name":"1. Create package","method":"Post","url":"https://x.test/packages"}
            """).RootElement, CancellationToken.None);

        using var doc = JsonDocument.Parse(output);
        Assert.Equal("pending_confirmation", doc.RootElement.GetProperty("status").GetString());
        var preview = doc.RootElement.GetProperty("preview").GetString()!;
        Assert.Contains("Collection Phone Notification/Signing", preview);
        Assert.Contains("Will be created: collection 'Phone Notification'", preview);
    }

    [Fact]
    public async Task ProposeCreate_ExistingCollectionMissingFolder_PreviewNamesTheMissingSegments()
    {
        var apiClient = new FakeApiClientAgentService();
        apiClient.CollectionsToReturn.Add(Collection("c1", "Phone Notification", folders: ["Signing"]));
        var tool = new ProposeApiRequestChangeTool(apiClient, new AgentActionCoordinator());

        var output = await tool.ExecuteAsync(JsonDocument.Parse("""
            {"operation":"create","collection_id":"c1","folder_path":"Signing/Onboarding",
             "name":"r","method":"Get","url":"https://x.test"}
            """).RootElement, CancellationToken.None);

        using var doc = JsonDocument.Parse(output);
        var preview = doc.RootElement.GetProperty("preview").GetString()!;
        Assert.Contains("Collection Phone Notification/Signing/Onboarding", preview);
        Assert.Contains("folder 'Signing/Onboarding'", preview);
        Assert.DoesNotContain("collection 'Phone Notification'", preview);
    }

    [Fact]
    public async Task ProposeCreate_ExistingCollectionAndFolder_PreviewHasNoWillCreateNote()
    {
        var apiClient = new FakeApiClientAgentService();
        apiClient.CollectionsToReturn.Add(Collection("c1", "Auth API", folders: ["Tokens"]));
        var tool = new ProposeApiRequestChangeTool(apiClient, new AgentActionCoordinator());

        var output = await tool.ExecuteAsync(JsonDocument.Parse("""
            {"operation":"create","collection_id":"c1","folder_path":"Tokens",
             "name":"r","method":"Get","url":"https://x.test"}
            """).RootElement, CancellationToken.None);

        using var doc = JsonDocument.Parse(output);
        Assert.DoesNotContain("Will be created", doc.RootElement.GetProperty("preview").GetString()!);
    }
}
