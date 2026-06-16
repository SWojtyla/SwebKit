using System.Text.Json;
using SwebKit.Core.Domain;
using SwebKit.Core.Serialization;

namespace SwebKit.Core.Configuration;

/// <summary>
/// Persists and loads API Client flows.
/// Local flows are stored in <c>api-flows.json</c>.
/// Linked-root flows are stored under the linked repository at <c>.swebkit-api/flows/&lt;flow&gt;.swebflow.json</c>.
/// Uses the atomic-write + <c>.bak</c> recovery pattern shared by all SwebKit repositories.
/// </summary>
public sealed class ApiFlowRepository
{
    private static readonly JsonSerializerOptions Options = new(SwebKitJsonOptions.Indented)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly LinkedCollectionRootRepository _linkedRootRepository;
    private ApiFlowsStore _localStore = new();

    public ApiFlowRepository(LinkedCollectionRootRepository linkedRootRepository)
    {
        _linkedRootRepository = linkedRootRepository;
    }

    // ─── Local Flows ─────────────────────────────────────────────────────────

    public IReadOnlyList<ApiFlowDefinition> LocalFlows => _localStore.Flows.AsReadOnly();

    public async Task LoadLocalAsync()
    {
        AppDataPaths.EnsureDirectoryExists();

        if (!AppDataFileStore.Exists(AppDataPaths.ApiFlowsJson))
        {
            _localStore = new ApiFlowsStore();
            return;
        }

        try
        {
            var result = await AppDataFileStore.LoadAsync(AppDataPaths.ApiFlowsJson, DeserializeLocalStore);
            _localStore = result.Value;
        }
        catch
        {
            _localStore = new ApiFlowsStore();
        }
    }

    public async Task SaveLocalAsync()
    {
        AppDataPaths.EnsureDirectoryExists();
        var json = JsonSerializer.Serialize(_localStore, Options);
        await AppDataFileStore.SaveAsync(AppDataPaths.ApiFlowsJson, json);
    }

    /// <summary>
    /// Adds a new local flow to the repository.
    /// </summary>
    public async Task<ApiFlowDefinition> AddLocalFlowAsync(ApiFlowDefinition flow)
    {
        flow.Id = Guid.NewGuid().ToString("N");
        flow.StorageScope = ApiFlowStorageScope.Local;
        flow.LinkedRootId = null;
        flow.LinkedRootPath = null;
        flow.CreatedAt = DateTimeOffset.UtcNow;
        flow.UpdatedAt = DateTimeOffset.UtcNow;

        _localStore.Flows.Add(flow);
        await SaveLocalAsync();
        return flow;
    }

    /// <summary>
    /// Updates an existing local flow.
    /// </summary>
    public async Task<bool> UpdateLocalFlowAsync(ApiFlowDefinition flow)
    {
        var idx = _localStore.Flows.FindIndex(f => f.Id == flow.Id);
        if (idx < 0) return false;

        flow.UpdatedAt = DateTimeOffset.UtcNow;
        _localStore.Flows[idx] = flow;
        await SaveLocalAsync();
        return true;
    }

    /// <summary>
    /// Deletes a local flow by ID.
    /// </summary>
    public async Task<bool> DeleteLocalFlowAsync(string flowId)
    {
        var removed = _localStore.Flows.RemoveAll(f => f.Id == flowId);
        if (removed == 0) return false;
        await SaveLocalAsync();
        return true;
    }

    /// <summary>
    /// Renames a local flow (updates Name only, not ID).
    /// </summary>
    public async Task<bool> RenameLocalFlowAsync(string flowId, string newName)
    {
        var flow = _localStore.Flows.Find(f => f.Id == flowId);
        if (flow is null) return false;

        flow.Name = newName;
        flow.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveLocalAsync();
        return true;
    }

    // ─── Linked-Root Flows ───────────────────────────────────────────────────

    /// <summary>
    /// Gets all flows stored in linked roots.
    /// </summary>
    public async Task<List<ApiFlowDefinition>> GetLinkedRootFlowsAsync()
    {
        var roots = await _linkedRootRepository.GetAllAsync();
        var flows = new List<ApiFlowDefinition>();

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root.LocalPath)) continue;

            var flowsDir = Path.Combine(root.LocalPath, ".swebkit-api", "flows");
            if (!Directory.Exists(flowsDir)) continue;

            foreach (var file in Directory.GetFiles(flowsDir, "*.swebflow.json"))
            {
                try
                {
                    var flow = await LoadLinkedFlowAsync(file, root);
                    if (flow is not null)
                    {
                        flows.Add(flow);
                    }
                }
                catch
                {
                    // Skip files that fail to load
                }
            }
        }

        return flows;
    }

    /// <summary>
    /// Gets flows for a specific linked root.
    /// </summary>
    public async Task<List<ApiFlowDefinition>> GetLinkedRootFlowsAsync(string linkedRootId)
    {
        var root = await _linkedRootRepository.GetByIdAsync(linkedRootId);
        if (root is null || string.IsNullOrWhiteSpace(root.LocalPath))
            return [];

        var flowsDir = Path.Combine(root.LocalPath, ".swebkit-api", "flows");
        if (!Directory.Exists(flowsDir))
            return [];

        var flows = new List<ApiFlowDefinition>();
        foreach (var file in Directory.GetFiles(flowsDir, "*.swebflow.json"))
        {
            try
            {
                var flow = await LoadLinkedFlowAsync(file, root);
                if (flow is not null)
                {
                    flows.Add(flow);
                }
            }
            catch
            {
                // Skip files that fail to load
            }
        }

        return flows;
    }

    /// <summary>
    /// Adds a new flow to a linked root.
    /// </summary>
    public async Task<ApiFlowDefinition> AddLinkedRootFlowAsync(string linkedRootId, ApiFlowDefinition flow)
    {
        var root = await _linkedRootRepository.GetByIdAsync(linkedRootId);
        if (root is null || string.IsNullOrWhiteSpace(root.LocalPath))
            throw new InvalidOperationException("Linked root not found or path not available.");

        flow.Id = Guid.NewGuid().ToString("N");
        flow.StorageScope = ApiFlowStorageScope.LinkedRoot;
        flow.LinkedRootId = linkedRootId;
        flow.LinkedRootPath = GetLinkedFlowPath(root.LocalPath, flow.Id, flow.Name);
        flow.CreatedAt = DateTimeOffset.UtcNow;
        flow.UpdatedAt = DateTimeOffset.UtcNow;

        var flowsDir = Path.Combine(root.LocalPath, ".swebkit-api", "flows");
        Directory.CreateDirectory(flowsDir);

        var filePath = Path.Combine(flowsDir, $"{flow.Id}.swebflow.json");
        var json = JsonSerializer.Serialize(flow, Options);

        // Atomic write for linked files
        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, filePath, overwrite: true);

        return flow;
    }

    /// <summary>
    /// Updates an existing linked-root flow.
    /// </summary>
    public async Task<bool> UpdateLinkedRootFlowAsync(ApiFlowDefinition flow)
    {
        if (flow.StorageScope != ApiFlowStorageScope.LinkedRoot || string.IsNullOrWhiteSpace(flow.LinkedRootId))
            return false;

        var root = await _linkedRootRepository.GetByIdAsync(flow.LinkedRootId);
        if (root is null || string.IsNullOrWhiteSpace(root.LocalPath))
            return false;

        flow.UpdatedAt = DateTimeOffset.UtcNow;

        var filePath = Path.Combine(root.LocalPath, ".swebkit-api", "flows", $"{flow.Id}.swebflow.json");
        if (!File.Exists(filePath))
            return false;

        var json = JsonSerializer.Serialize(flow, Options);

        // Atomic write
        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, filePath, overwrite: true);

        return true;
    }

    /// <summary>
    /// Deletes a linked-root flow by ID.
    /// </summary>
    public async Task<bool> DeleteLinkedRootFlowAsync(string flowId, string linkedRootId)
    {
        var root = await _linkedRootRepository.GetByIdAsync(linkedRootId);
        if (root is null || string.IsNullOrWhiteSpace(root.LocalPath))
            return false;

        var filePath = Path.Combine(root.LocalPath, ".swebkit-api", "flows", $"{flowId}.swebflow.json");
        if (!File.Exists(filePath))
            return false;

        File.Delete(filePath);
        return true;
    }

    /// <summary>
    /// Renames a linked-root flow (updates Name only, not ID or file name).
    /// </summary>
    public async Task<bool> RenameLinkedRootFlowAsync(string flowId, string linkedRootId, string newName)
    {
        var flow = await GetLinkedRootFlowByIdAsync(flowId, linkedRootId);
        if (flow is null) return false;

        flow.Name = newName;
        flow.UpdatedAt = DateTimeOffset.UtcNow;

        return await UpdateLinkedRootFlowAsync(flow);
    }

    /// <summary>
    /// Gets a specific linked-root flow by ID.
    /// </summary>
    public async Task<ApiFlowDefinition?> GetLinkedRootFlowByIdAsync(string flowId, string linkedRootId)
    {
        var flows = await GetLinkedRootFlowsAsync(linkedRootId);
        return flows.Find(f => f.Id == flowId);
    }

    // ─── All Flows (Local + Linked) ───────────────────────────────────────────

    /// <summary>
    /// Gets all flows (local + linked-root).
    /// </summary>
    public async Task<List<ApiFlowDefinition>> GetAllFlowsAsync()
    {
        var allFlows = new List<ApiFlowDefinition>(_localStore.Flows);
        var linkedFlows = await GetLinkedRootFlowsAsync();
        allFlows.AddRange(linkedFlows);
        return allFlows;
    }

    /// <summary>
    /// Gets a flow by ID, regardless of storage scope.
    /// </summary>
    public async Task<ApiFlowDefinition?> GetFlowByIdAsync(string flowId)
    {
        // Check local first
        var localFlow = _localStore.Flows.Find(f => f.Id == flowId);
        if (localFlow is not null) return localFlow;

        // Check linked roots
        var roots = await _linkedRootRepository.GetAllAsync();
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root.LocalPath)) continue;
            var flow = await GetLinkedRootFlowByIdAsync(flowId, root.Id);
            if (flow is not null) return flow;
        }

        return null;
    }

    // ─── Collision-Safe Operations ────────────────────────────────────────────

    /// <summary>
    /// Creates a flow with collision-safe naming (appends suffix if name exists).
    /// </summary>
    public async Task<ApiFlowDefinition> CreateFlowWithUniqueNameAsync(
        string name,
        ApiFlowStorageScope scope,
        string? linkedRootId = null)
    {
        var uniqueName = name;
        var counter = 1;

        while (await FlowNameExistsAsync(uniqueName, scope, linkedRootId))
        {
            uniqueName = $"{name} ({counter})";
            counter++;
        }

        var flow = new ApiFlowDefinition
        {
            Name = uniqueName,
            StorageScope = scope,
            LinkedRootId = linkedRootId,
            Steps = [],
            FailurePolicy = ApiFlowFailurePolicy.StopOnFailure,
            DefaultTimeoutSeconds = 30,
        };

        if (scope == ApiFlowStorageScope.Local)
        {
            return await AddLocalFlowAsync(flow);
        }
        else
        {
            if (linkedRootId is null)
                throw new InvalidOperationException("Linked root ID is required for linked-root flows.");
            return await AddLinkedRootFlowAsync(linkedRootId, flow);
        }
    }

    private async Task<bool> FlowNameExistsAsync(string name, ApiFlowStorageScope scope, string? linkedRootId)
    {
        if (scope == ApiFlowStorageScope.Local)
        {
            return _localStore.Flows.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            if (linkedRootId is null) return false;
            var flows = await GetLinkedRootFlowsAsync(linkedRootId);
            return flows.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ─── Helper Methods ───────────────────────────────────────────────────────

    private static string GetLinkedFlowPath(string rootPath, string flowId, string flowName)
    {
        // Sanitize name for filename
        var safeName = string.Join("-", flowName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(".swebkit-api", "flows", $"{flowId}.swebflow.json");
    }

    private static async Task<ApiFlowDefinition?> LoadLinkedFlowAsync(string filePath, Domain.LinkedCollectionRoot root)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var flow = JsonSerializer.Deserialize<ApiFlowDefinition>(json, Options);
            if (flow is null) return null;

            flow.StorageScope = ApiFlowStorageScope.LinkedRoot;
            flow.LinkedRootId = root.Id;
            flow.LinkedRootPath = Path.Combine(".swebkit-api", "flows", Path.GetFileName(filePath));

            return flow;
        }
        catch
        {
            return null;
        }
    }

    private static ApiFlowsStore DeserializeLocalStore(string json) =>
        JsonSerializer.Deserialize<ApiFlowsStore>(json, Options) ?? new ApiFlowsStore();
}
