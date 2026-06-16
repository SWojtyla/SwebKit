using System.Text.Json;
using SwebKit.Core.Configuration;
using SwebKit.Core.Domain;

namespace SwebKit.Core.Tests;

public class ApiFlowRepositoryTests
{
    private readonly string _testRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SwebKit-Test",
        Guid.NewGuid().ToString("N"));

    private readonly LinkedCollectionRootRepository _linkedRootRepo;
    private readonly ApiFlowRepository _repository;

    public ApiFlowRepositoryTests()
    {
        // Setup test environment
        Directory.CreateDirectory(_testRoot);
        Environment.SetEnvironmentVariable("SWEBKIT_APPDATA_ROOT", _testRoot);

        _linkedRootRepo = new LinkedCollectionRootRepository();
        _repository = new ApiFlowRepository(_linkedRootRepo);
    }

    // ─── Local Flow Tests ─────────────────────────────────────────────────────

    [Fact]
    public async Task AddLocalFlowAsync_CreatesFlowWithIdAndTimestamps()
    {
        await _repository.LoadLocalAsync();

        var flow = new ApiFlowDefinition
        {
            Name = "Test Flow",
            Description = "A test flow",
            Steps = [],
            FailurePolicy = ApiFlowFailurePolicy.StopOnFailure,
        };

        var result = await _repository.AddLocalFlowAsync(flow);

        Assert.NotEmpty(result.Id);
        Assert.Equal("Test Flow", result.Name);
        Assert.Equal(ApiFlowStorageScope.Local, result.StorageScope);
        Assert.Null(result.LinkedRootId);
        Assert.Null(result.LinkedRootPath);
        Assert.NotEqual(default, result.CreatedAt);
        Assert.NotEqual(default, result.UpdatedAt);
    }

    [Fact]
    public async Task GetAllFlowsAsync_ReturnsLocalFlows()
    {
        await _repository.LoadLocalAsync();

        var flow1 = await _repository.AddLocalFlowAsync(new ApiFlowDefinition
        {
            Name = "Flow 1",
            Steps = [],
        });

        var flow2 = await _repository.AddLocalFlowAsync(new ApiFlowDefinition
        {
            Name = "Flow 2",
            Steps = [],
        });

        var allFlows = await _repository.GetAllFlowsAsync();

        Assert.Equal(2, allFlows.Count);
        Assert.Contains(allFlows, f => f.Id == flow1.Id);
        Assert.Contains(allFlows, f => f.Id == flow2.Id);
    }

    [Fact]
    public async Task UpdateLocalFlowAsync_UpdatesFlowAndTimestamps()
    {
        await _repository.LoadLocalAsync();

        var flow = await _repository.AddLocalFlowAsync(new ApiFlowDefinition
        {
            Name = "Original",
            Steps = [],
        });

        var originalUpdatedAt = flow.UpdatedAt;
        await Task.Delay(10); // Ensure timestamp changes

        flow.Name = "Updated";
        var result = await _repository.UpdateLocalFlowAsync(flow);

        Assert.True(result);
        Assert.Equal("Updated", flow.Name);
        Assert.NotEqual(originalUpdatedAt, flow.UpdatedAt);
    }

    [Fact]
    public async Task DeleteLocalFlowAsync_RemovesFlow()
    {
        await _repository.LoadLocalAsync();

        var flow = await _repository.AddLocalFlowAsync(new ApiFlowDefinition
        {
            Name = "To Delete",
            Steps = [],
        });

        var result = await _repository.DeleteLocalFlowAsync(flow.Id);

        Assert.True(result);
        var allFlows = await _repository.GetAllFlowsAsync();
        Assert.DoesNotContain(allFlows, f => f.Id == flow.Id);
    }

    [Fact]
    public async Task CreateFlowWithUniqueNameAsync_AppendsSuffixOnCollision()
    {
        await _repository.LoadLocalAsync();

        var flow1 = await _repository.CreateFlowWithUniqueNameAsync(
            "My Flow", ApiFlowStorageScope.Local, null);

        var flow2 = await _repository.CreateFlowWithUniqueNameAsync(
            "My Flow", ApiFlowStorageScope.Local, null);

        Assert.Equal("My Flow", flow1.Name);
        Assert.Equal("My Flow (1)", flow2.Name);
    }

    [Fact]
    public async Task RenameLocalFlowAsync_UpdatesNameOnly()
    {
        await _repository.LoadLocalAsync();

        var flow = await _repository.AddLocalFlowAsync(new ApiFlowDefinition
        {
            Name = "Original",
            Steps = [],
        });

        var result = await _repository.RenameLocalFlowAsync(flow.Id, "Renamed");

        Assert.True(result);
        var updated = _repository.LocalFlows.First(f => f.Id == flow.Id);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(flow.Id, updated.Id); // ID unchanged
    }

    // ─── Serialization Tests ───────────────────────────────────────────────────

    [Fact]
    public async Task LocalFlows_AreSerializedAndDeserializedCorrectly()
    {
        await _repository.LoadLocalAsync();

        var originalFlow = new ApiFlowDefinition
        {
            Name = "Complex Flow",
            Description = "With all properties",
            StorageScope = ApiFlowStorageScope.Local,
            FailurePolicy = ApiFlowFailurePolicy.ContinueOnFailure,
            DefaultTimeoutSeconds = 60,
            Steps = new List<ApiFlowStep>
            {
                new ApiFlowStep
                {
                    Id = "step1",
                    Name = "Step 1",
                    Order = 0,
                    IsEnabled = true,
                    RequestReference = new ApiRequestReference
                    {
                        Id = "req1",
                        SourceKind = ApiRequestReferenceKind.LocalCollection,
                        SourceId = "col1",
                        RequestId = "req1",
                        RequestName = "Test Request",
                        SourceName = "Test Collection"
                    },
                    VariableOverrides = new List<ApiFlowVariableOverride>
                    {
                        new ApiFlowVariableOverride { Key = "var1", Value = "value1", IsSecret = false }
                    },
                    CaptureMappings = new List<ApiFlowCaptureMapping>
                    {
                        new ApiFlowCaptureMapping
                        {
                            Id = "cap1",
                            Source = ApiFlowCaptureSource.BodyJsonPath,
                            JsonPath = "$.token",
                            TargetVariable = "authToken",
                            IsEnabled = true
                        }
                    }
                }
            },
            VariableOverrides = new List<ApiFlowVariableOverride>
            {
                new ApiFlowVariableOverride { Key = "globalVar", Value = "globalValue", IsSecret = true }
            }
        };

        await _repository.AddLocalFlowAsync(originalFlow);
        await _repository.SaveLocalAsync();

        // Create new repository to test deserialization
        var newRepo = new ApiFlowRepository(_linkedRootRepo);
        await newRepo.LoadLocalAsync();

        var deserialized = newRepo.LocalFlows.First();

        Assert.Equal(originalFlow.Name, deserialized.Name);
        Assert.Equal(originalFlow.Description, deserialized.Description);
        Assert.Equal(originalFlow.FailurePolicy, deserialized.FailurePolicy);
        Assert.Equal(originalFlow.DefaultTimeoutSeconds, deserialized.DefaultTimeoutSeconds);
        Assert.Single(deserialized.Steps);
        Assert.Equal("Step 1", deserialized.Steps[0].Name);
        Assert.Equal(ApiFlowCaptureSource.BodyJsonPath, deserialized.Steps[0].CaptureMappings[0].Source);
    }

    // ─── Linked Root Flow Tests ────────────────────────────────────────────────

    [Fact]
    public async Task AddLinkedRootFlowAsync_CreatesFileInLinkedRoot()
    {
        var rootPath = Path.Combine(_testRoot, "linked-repo");
        Directory.CreateDirectory(rootPath);

        var root = new LinkedCollectionRoot
        {
            Id = "root1",
            Name = "Test Repo",
            LocalPath = rootPath,
            GitRemoteUrl = "https://github.com/test/repo"
        };

        // Manually add root to repository (bypassing normal methods for test)
        var rootsField = typeof(LinkedCollectionRootRepository)
            .GetField("_store", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var store = (LinkedCollectionRootsStore)rootsField!.GetValue(_linkedRootRepo)!;
        store.Roots.Add(root);

        var flow = new ApiFlowDefinition
        {
            Name = "Linked Flow",
            Steps = [],
        };

        var result = await _repository.AddLinkedRootFlowAsync("root1", flow);

        Assert.NotEmpty(result.Id);
        Assert.Equal(ApiFlowStorageScope.LinkedRoot, result.StorageScope);
        Assert.Equal("root1", result.LinkedRootId);

        // Verify file was created
        var expectedPath = Path.Combine(rootPath, ".swebkit-api", "flows", $"{result.Id}.swebflow.json");
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public async Task GetLinkedRootFlowsAsync_ReturnsFlowsFromLinkedRoot()
    {
        var rootPath = Path.Combine(_testRoot, "linked-repo-2");
        Directory.CreateDirectory(rootPath);

        var root = new LinkedCollectionRoot
        {
            Id = "root2",
            Name = "Test Repo 2",
            LocalPath = rootPath,
            GitRemoteUrl = "https://github.com/test/repo2"
        };

        var rootsField = typeof(LinkedCollectionRootRepository)
            .GetField("_store", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var store = (LinkedCollectionRootsStore)rootsField!.GetValue(_linkedRootRepo)!;
        store.Roots.Add(root);

        // Create a flow file directly
        var flowsDir = Path.Combine(rootPath, ".swebkit-api", "flows");
        Directory.CreateDirectory(flowsDir);
        var flow = new ApiFlowDefinition
        {
            Id = "flow1",
            Name = "Direct Flow",
            StorageScope = ApiFlowStorageScope.LinkedRoot,
            LinkedRootId = "root2",
            LinkedRootPath = Path.Combine(".swebkit-api", "flows", "flow1.swebflow.json"),
            Steps = []
        };
        var flowPath = Path.Combine(flowsDir, "flow1.swebflow.json");
        await File.WriteAllTextAsync(flowPath, JsonSerializer.Serialize(flow));

        var linkedFlows = await _repository.GetLinkedRootFlowsAsync("root2");

        Assert.Single(linkedFlows);
        Assert.Equal("Direct Flow", linkedFlows[0].Name);
    }

    // ─── Flow By ID Tests ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetFlowByIdAsync_ReturnsLocalFlow()
    {
        await _repository.LoadLocalAsync();

        var flow = await _repository.AddLocalFlowAsync(new ApiFlowDefinition
        {
            Name = "Findable Flow",
            Steps = [],
        });

        var result = await _repository.GetFlowByIdAsync(flow.Id);

        Assert.NotNull(result);
        Assert.Equal(flow.Id, result.Id);
    }

    [Fact]
    public async Task GetFlowByIdAsync_ReturnsNullForNonExistent()
    {
        var result = await _repository.GetFlowByIdAsync("non-existent-id");
        Assert.Null(result);
    }

    // ─── Cleanup ──────────────────────────────────────────────────────────────

    public async Task DisposeAsync()
    {
        try
        {
            Directory.Delete(_testRoot, true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
