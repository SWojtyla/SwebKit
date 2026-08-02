using System.Text.Json;
using SwebKit.Core.Abstractions;
using SwebKit.Core.Domain;
using SwebKit.Agents.Tools;

namespace SwebKit.Agents.Tools.ApiClient;

/// <summary>
/// Searches and lists API requests across all collections.
/// </summary>
public sealed class SearchApiRequestsTool : IAgentTool
{
    private readonly IApiClientAgentService _apiClient;

    public SearchApiRequestsTool(IApiClientAgentService apiClient) => _apiClient = apiClient;

    public string Name => "search_api_requests";
    public string Description => "Search and list API requests across all collections. Returns request IDs, names, methods, URLs, and collection origin.";

    private static readonly JsonElement Schema = AgentToolSchema.Parse("""
    {
        "type": "object",
        "properties": {
            "query": {
                "type": "string",
                "description": "Optional search query to filter by name, URL, or method."
            }
        },
        "additionalProperties": false
    }
    """);

    public JsonElement ParametersSchema => Schema;

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        var query = arguments.TryGetProperty("query", out var q) ? q.GetString() : null;
        var results = await _apiClient.SearchRequestsAsync(query, ct);

        if (results.Count == 0)
            return """{"count":0,"requests":[],"message":"No requests found."}""";

        var requests = results.Select(r => new
        {
            id = r.Id,
            name = r.Name,
            method = r.Method.ToString(),
            url = r.Url,
            collection = r.CollectionName,
            origin = r.CollectionOrigin,
            folder = r.FolderPath,
        });

        return JsonSerializer.Serialize(new { count = results.Count, requests });
    }
}

/// <summary>
/// Reads a single API request by ID with secrets masked.
/// </summary>
public sealed class GetApiRequestTool : IAgentTool
{
    private readonly IApiClientAgentService _apiClient;

    public GetApiRequestTool(IApiClientAgentService apiClient) => _apiClient = apiClient;

    public string Name => "get_api_request";
    public string Description => "Read a single API request by ID. Returns full request details with secrets masked.";

    private static readonly JsonElement Schema = AgentToolSchema.Parse("""
    {
        "type": "object",
        "properties": {
            "request_id": {
                "type": "string",
                "description": "The ID of the request to read."
            }
        },
        "required": ["request_id"],
        "additionalProperties": false
    }
    """);

    public JsonElement ParametersSchema => Schema;

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("request_id", out var idProp))
            return """{"error":"Missing required parameter 'request_id'."}""";

        var requestId = idProp.GetString();
        if (string.IsNullOrEmpty(requestId))
            return """{"error":"Parameter 'request_id' must be a non-empty string."}""";

        var snapshot = await _apiClient.GetRequestAsync(requestId, ct);
        if (snapshot is null)
            return $$"""{"error":"Request '{{requestId}}' not found."}""";

        var result = new
        {
            id = snapshot.Id,
            name = snapshot.Name,
            method = snapshot.Method.ToString(),
            url = snapshot.Url,
            collection = snapshot.CollectionName,
            origin = snapshot.CollectionOrigin,
            folder = snapshot.FolderPath,
            headers = snapshot.Headers.Select(h => new { key = h.Key, value = h.Value }),
            query_params = snapshot.QueryParams.Select(q => new { key = q.Key, value = q.Value }),
            body_content_type = snapshot.BodyContentType,
            body_preview = snapshot.BodyPreview,
            auth_type = snapshot.AuthType,
            updated_at = snapshot.UpdatedAt.ToString("yyyy-MM-dd HH:mm UTC"),
        };

        return JsonSerializer.Serialize(result);
    }
}

/// <summary>
/// Proposes a change (create/update/duplicate/move/rename) without applying it.
/// Returns a pending action for user confirmation.
/// </summary>
public sealed class ProposeApiRequestChangeTool : IAgentTool
{
    private readonly IApiClientAgentService _apiClient;
    private readonly IAgentActionCoordinator _coordinator;

    public ProposeApiRequestChangeTool(IApiClientAgentService apiClient, IAgentActionCoordinator coordinator)
    {
        _apiClient = apiClient;
        _coordinator = coordinator;
    }

    public string Name => "propose_api_request_change";
    public string Description => "Propose a change to API requests (create, update, duplicate, or move). Returns a pending action for user confirmation. No changes are applied until confirmed.";
    public ToolKind Kind => ToolKind.Mutate;
    public ToolRisk Risk => ToolRisk.Low;

    private static readonly JsonElement Schema = AgentToolSchema.Parse("""
    {
        "type": "object",
        "properties": {
            "operation": {
                "type": "string",
                "enum": ["create", "update", "duplicate", "move"],
                "description": "The type of change to propose."
            },
            "request_id": {
                "type": "string",
                "description": "ID of the request (for update, duplicate, move)."
            },
            "collection_id": {
                "type": "string",
                "description": "ID of the target collection (for create)."
            },
            "folder_path": {
                "type": "string",
                "description": "Folder path within the collection (for create, move)."
            },
            "name": {
                "type": "string",
                "description": "New name for the request (for create, update)."
            },
            "method": {
                "type": "string",
                "enum": ["Get", "Post", "Put", "Patch", "Delete", "Head", "Options"],
                "description": "HTTP method (for create, update)."
            },
            "url": {
                "type": "string",
                "description": "Request URL (for create, update)."
            },
            "new_index": {
                "type": "integer",
                "description": "Target position for move (0-based)."
            }
        },
        "required": ["operation"],
        "additionalProperties": false
    }
    """);

    public JsonElement ParametersSchema => Schema;

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("operation", out var opProp))
            return """{"error":"Missing required parameter 'operation'."}""";

        var operation = opProp.GetString();
        if (string.IsNullOrEmpty(operation))
            return """{"error":"Parameter 'operation' must be a non-empty string."}""";

        var actionId = Guid.NewGuid().ToString("N");
        string summary, target, preview;
        AgentActionType actionType;
        AgentActionRisk risk = AgentActionRisk.Low;

        switch (operation.ToLowerInvariant())
        {
            case "create":
            {
                if (!arguments.TryGetProperty("collection_id", out var collId))
                    return """{"error":"Missing required parameter 'collection_id' for create operation."}""";
                if (!arguments.TryGetProperty("name", out var nameProp) || nameProp.GetString() is not { } name)
                    return """{"error":"Missing required parameter 'name' for create operation."}""";
                var method = arguments.TryGetProperty("method", out var m) && Enum.TryParse<ApiRequestMethod>(m.GetString(), out var parsed) ? parsed : ApiRequestMethod.Get;
                var url = arguments.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var folderPath = arguments.TryGetProperty("folder_path", out var f) ? f.GetString() : null;

                actionType = AgentActionType.CreateRequest;
                target = $"Collection {collId}" + (folderPath is not null ? $"/{folderPath}" : "");
                summary = $"Create request '{name}' ({method} {url})";
                preview = $"Name: {name}\nMethod: {method}\nURL: {url}\nLocation: {target}";
                break;
            }

            case "update":
            {
                if (!arguments.TryGetProperty("request_id", out var reqId) || reqId.GetString() is not { } requestId)
                    return """{"error":"Missing required parameter 'request_id' for update operation."}""";

                var snapshot = await _apiClient.GetRequestAsync(requestId, ct);
                if (snapshot is null)
                    return $$"""{"error":"Request '{{requestId}}' not found."}""";

                actionType = AgentActionType.UpdateRequest;
                target = $"Request '{snapshot.Name}' ({snapshot.Id})";
                var changes = new List<string>();
                if (arguments.TryGetProperty("name", out var n) && n.GetString() is { } newName) changes.Add($"name: {snapshot.Name} → {newName}");
                if (arguments.TryGetProperty("method", out var m) && Enum.TryParse<ApiRequestMethod>(m.GetString(), out var newMethod)) changes.Add($"method: {snapshot.Method} → {newMethod}");
                if (arguments.TryGetProperty("url", out var u) && u.GetString() is { } newUrl) changes.Add($"url: {snapshot.Url} → {newUrl}");

                summary = $"Update request '{snapshot.Name}': {string.Join(", ", changes)}";
                preview = $"Changes:\n{string.Join("\n", changes)}";
                break;
            }

            case "duplicate":
            {
                if (!arguments.TryGetProperty("request_id", out var reqId) || reqId.GetString() is not { } requestId)
                    return """{"error":"Missing required parameter 'request_id' for duplicate operation."}""";

                var snapshot = await _apiClient.GetRequestAsync(requestId, ct);
                if (snapshot is null)
                    return $$"""{"error":"Request '{{requestId}}' not found."}""";

                actionType = AgentActionType.DuplicateRequest;
                target = $"Request '{snapshot.Name}' ({snapshot.Id})";
                summary = $"Duplicate request '{snapshot.Name}' as '{snapshot.Name} (copy)'";
                preview = $"Source: {snapshot.Name} ({snapshot.Method} {snapshot.Url})\nCopy will be: {snapshot.Name} (copy)";
                break;
            }

            case "move":
            {
                if (!arguments.TryGetProperty("request_id", out var reqId) || reqId.GetString() is not { } requestId)
                    return """{"error":"Missing required parameter 'request_id' for move operation."}""";

                var snapshot = await _apiClient.GetRequestAsync(requestId, ct);
                if (snapshot is null)
                    return $$"""{"error":"Request '{{requestId}}' not found."}""";

                var targetFolder = arguments.TryGetProperty("folder_path", out var f) ? f.GetString() : null;
                var newIndex = arguments.TryGetProperty("new_index", out var ni) ? ni.GetInt32() : (int?)null;

                actionType = AgentActionType.MoveRequest;
                target = $"Request '{snapshot.Name}' ({snapshot.Id})";
                summary = $"Move request '{snapshot.Name}' to {(targetFolder ?? "root")}{(newIndex is not null ? $" at index {newIndex}" : "")}";
                preview = $"From: {snapshot.FolderPath ?? "root"}\nTo: {targetFolder ?? "root"}{(newIndex is not null ? $" (index {newIndex})" : "")}";
                break;
            }

            default:
                return $$"""{"error":"Unknown operation '{{operation}}'. Supported: create, update, duplicate, move."}""";
        }

        var action = new PendingAgentAction
        {
            Id = actionId,
            Type = actionType,
            Summary = summary,
            Target = target,
            Risk = risk,
            Preview = preview,
            ExpectedFingerprint = null, // Set at apply time for freshness check
            // The applier (ApiClientActionExecutor) reads exact field values back out of this at
            // apply time rather than re-parsing `preview`'s human-readable diff text.
            Payload = arguments.Clone(),
        };

        _coordinator.RegisterAction(action);

        return JsonSerializer.Serialize(new
        {
            action_id = actionId,
            status = "pending_confirmation",
            summary,
            preview,
            risk = risk.ToString(),
            expires_at = action.ExpiresAt.ToString("yyyy-MM-dd HH:mm UTC"),
            message = "Action proposed. User must confirm before it is applied.",
        });
    }
}

/// <summary>
/// Proposes deletion of a request. Separate tool to make destruction explicit.
/// </summary>
public sealed class ProposeApiRequestDeleteTool : IAgentTool
{
    private readonly IApiClientAgentService _apiClient;
    private readonly IAgentActionCoordinator _coordinator;

    public ProposeApiRequestDeleteTool(IApiClientAgentService apiClient, IAgentActionCoordinator coordinator)
    {
        _apiClient = apiClient;
        _coordinator = coordinator;
    }

    public string Name => "propose_api_request_delete";
    public string Description => "Propose deletion of an API request. This is a separate tool to make destruction explicit. Returns a pending action for user confirmation.";
    public ToolKind Kind => ToolKind.Mutate;
    public ToolRisk Risk => ToolRisk.High;

    private static readonly JsonElement Schema = AgentToolSchema.Parse("""
    {
        "type": "object",
        "properties": {
            "request_id": {
                "type": "string",
                "description": "ID of the request to delete."
            }
        },
        "required": ["request_id"],
        "additionalProperties": false
    }
    """);

    public JsonElement ParametersSchema => Schema;

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("request_id", out var reqId) || reqId.GetString() is not { } requestId)
            return """{"error":"Missing required parameter 'request_id'."}""";

        var snapshot = await _apiClient.GetRequestAsync(requestId, ct);
        if (snapshot is null)
            return $$"""{"error":"Request '{{requestId}}' not found."}""";

        var actionId = Guid.NewGuid().ToString("N");
        var action = new PendingAgentAction
        {
            Id = actionId,
            Type = AgentActionType.DeleteRequest,
            Summary = $"Delete request '{snapshot.Name}'",
            Target = $"Request '{snapshot.Name}' ({snapshot.Id})",
            Risk = AgentActionRisk.High,
            Preview = $"Name: {snapshot.Name}\nMethod: {snapshot.Method}\nURL: {snapshot.Url}\nCollection: {snapshot.CollectionName}\nFolder: {snapshot.FolderPath ?? "root"}",
            ExpectedFingerprint = snapshot.UpdatedAt.ToString("O"),
        };

        _coordinator.RegisterAction(action);

        return JsonSerializer.Serialize(new
        {
            action_id = actionId,
            status = "pending_confirmation",
            summary = action.Summary,
            preview = action.Preview,
            risk = "High",
            expires_at = action.ExpiresAt.ToString("yyyy-MM-dd HH:mm UTC"),
            message = "Deletion proposed. User must explicitly confirm before the request is removed.",
        });
    }
}

/// <summary>
/// Prepares HTTP request execution — resolves variables, masks secrets, creates confirmable action.
/// </summary>
public sealed class PrepareApiRequestExecutionTool : IAgentTool
{
    private readonly IApiClientAgentService _apiClient;
    private readonly IAgentActionCoordinator _coordinator;

    public PrepareApiRequestExecutionTool(IApiClientAgentService apiClient, IAgentActionCoordinator coordinator)
    {
        _apiClient = apiClient;
        _coordinator = coordinator;
    }

    public string Name => "prepare_api_request_execution";
    public string Description => "Prepare execution of an API request. Resolves variables, masks auth/secrets, and creates a confirmable action. No HTTP request is sent until the user confirms.";
    public ToolKind Kind => ToolKind.Mutate;
    public ToolRisk Risk => ToolRisk.High;

    private static readonly JsonElement Schema = AgentToolSchema.Parse("""
    {
        "type": "object",
        "properties": {
            "request_id": {
                "type": "string",
                "description": "ID of the request to execute."
            }
        },
        "required": ["request_id"],
        "additionalProperties": false
    }
    """);

    public JsonElement ParametersSchema => Schema;

    public async Task<string> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        if (!arguments.TryGetProperty("request_id", out var reqId) || reqId.GetString() is not { } requestId)
            return """{"error":"Missing required parameter 'request_id'."}""";

        var snapshot = await _apiClient.GetRequestAsync(requestId, ct);
        if (snapshot is null)
            return $$"""{"error":"Request '{{requestId}}' not found."}""";

        var actionId = Guid.NewGuid().ToString("N");
        var action = new PendingAgentAction
        {
            Id = actionId,
            Type = AgentActionType.ExecuteHttpRequest,
            Summary = $"Execute {snapshot.Method} {snapshot.Url}",
            Target = $"Request '{snapshot.Name}' ({snapshot.Id})",
            Risk = AgentActionRisk.High,
            Preview = $"Method: {snapshot.Method}\nURL: {snapshot.Url}\nHeaders: {snapshot.Headers.Count} (secrets masked)\nBody: {snapshot.BodyContentType ?? "none"}\n\nWARNING: This will send a real HTTP request to an external server.",
            ExpectedFingerprint = snapshot.UpdatedAt.ToString("O"),
        };

        _coordinator.RegisterAction(action);

        return JsonSerializer.Serialize(new
        {
            action_id = actionId,
            status = "pending_confirmation",
            summary = action.Summary,
            preview = action.Preview,
            risk = "High",
            expires_at = action.ExpiresAt.ToString("yyyy-MM-dd HH:mm UTC"),
            message = "Execution prepared. User must confirm before the HTTP request is sent.",
        });
    }
}
